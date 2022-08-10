﻿using MassSpectrometry;
using MzLibUtil;
using Proteomics;
using Proteomics.Fragmentation;
using Proteomics.ProteolyticDigestion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EngineLayer.ClassicSearch
{
    public class ClassicSearchEngine : MetaMorpheusEngine
    {
        private readonly SpectralLibrary SpectralLibrary;
        private readonly MassDiffAcceptor SearchMode;
        private readonly List<Protein> Proteins;
        private readonly List<Modification> FixedModifications;
        private readonly List<Modification> VariableModifications;
        private readonly List<SilacLabel> SilacLabels;
        private readonly (SilacLabel StartLabel, SilacLabel EndLabel)? TurnoverLabels;
        private readonly PeptideSpectralMatch[] PeptideSpectralMatches;
        private readonly Ms2ScanWithSpecificMass[] ArrayOfSortedMS2Scans;
        private readonly double[] MyScanPrecursorMasses;
        private readonly bool WriteSpectralLibrary;
        private readonly bool DecoyOnTheFly;

        public ClassicSearchEngine(PeptideSpectralMatch[] globalPsms, Ms2ScanWithSpecificMass[] arrayOfSortedMS2Scans,
            List<Modification> variableModifications, List<Modification> fixedModifications, List<SilacLabel> silacLabels, SilacLabel startLabel, SilacLabel endLabel,
            List<Protein> proteinList, MassDiffAcceptor searchMode, CommonParameters commonParameters, List<(string FileName, CommonParameters Parameters)> fileSpecificParameters,
            SpectralLibrary spectralLibrary, List<string> nestedIds, bool writeSpectralLibrary, bool decoyOnTheFly = false)
            : base(commonParameters, fileSpecificParameters, nestedIds)
        {
            PeptideSpectralMatches = globalPsms;
            ArrayOfSortedMS2Scans = arrayOfSortedMS2Scans;
            MyScanPrecursorMasses = arrayOfSortedMS2Scans.Select(b => b.PrecursorMass).ToArray();
            VariableModifications = variableModifications;
            FixedModifications = fixedModifications;
            SilacLabels = silacLabels;
            if (startLabel != null || endLabel != null) //else it's null
            {
                TurnoverLabels = (startLabel, endLabel);
            }

            SearchMode = searchMode;
            SpectralLibrary = spectralLibrary;
            WriteSpectralLibrary = writeSpectralLibrary;
            DecoyOnTheFly = decoyOnTheFly;

            // if we're doing a spectral library search, we can skip the reverse protein decoys generated by metamorpheus.
            // we will generate reverse peptide decoys w/ in the code just below this (and calculate spectral angles for them later).
            // we have to generate the reverse peptides instead of the usual reverse proteins because we generate decoy spectral
            // library spectra from their corresponding paired target peptides
            // for DecoyOnTheFly, we also remove decoys from the spectral library
            Proteins = spectralLibrary == null || DecoyOnTheFly == true ? proteinList : proteinList.Where(p => !p.IsDecoy).ToList();
        }

        protected override MetaMorpheusEngineResults RunSpecific()
        {
            Status("Getting ms2 scans...");

            double proteinsSearched = 0;
            int oldPercentProgress = 0;

            // one lock for each MS2 scan; a scan can only be accessed by one thread at a time
            var myLocks = new object[PeptideSpectralMatches.Length];
            for (int i = 0; i < myLocks.Length; i++)
            {
                myLocks[i] = new object();
            }

            Status("Performing classic search...");

            if (Proteins.Any())
            {
                int maxThreadsPerFile = CommonParameters.MaxThreadsToUsePerFile;
                int[] threads = Enumerable.Range(0, maxThreadsPerFile).ToArray();
                Parallel.ForEach(threads, (i) =>
                {
                    var targetFragmentsForEachDissociationType = new Dictionary<DissociationType, List<Product>>();
                    var decoyFragmentsForEachDissociationType = new Dictionary<DissociationType, List<Product>>();

                    // check if we're supposed to autodetect dissociation type from the scan header or not
                    if (CommonParameters.DissociationType == DissociationType.Autodetect)
                    {
                        foreach (var item in GlobalVariables.AllSupportedDissociationTypes.Where(p => p.Value != DissociationType.Autodetect))
                        {
                            targetFragmentsForEachDissociationType.Add(item.Value, new List<Product>());
                            decoyFragmentsForEachDissociationType.Add(item.Value, new List<Product>());
                        }
                    }
                    else
                    {
                        targetFragmentsForEachDissociationType.Add(CommonParameters.DissociationType, new List<Product>());
                        decoyFragmentsForEachDissociationType.Add(CommonParameters.DissociationType, new List<Product>());
                    }

                    for (; i < Proteins.Count; i += maxThreadsPerFile)
                    {
                        // Stop loop if canceled
                        if (GlobalVariables.StopLoops) { return; }

                        // digest each protein into peptides and search for each peptide in all spectra within precursor mass tolerance
                        foreach (PeptideWithSetModifications peptide in Proteins[i].Digest(CommonParameters.DigestionParams, FixedModifications, VariableModifications, SilacLabels, TurnoverLabels))
                        {
                            PeptideWithSetModifications generatedOnTheFlyDecoy = null;
                            // Do rev check similarity, do scrambled, check sim, do mirrored
                            if (DecoyOnTheFly == true)
                            {
                                // The change in this region is non-conservative.
                                // This changes how decoys are generated when using a spectral library
                                // in addition to adding functionality for DecoyOnTheFly                                
                                int[] newAAlocations = new int[peptide.BaseSequence.Length];
                                generatedOnTheFlyDecoy = peptide.GetReverseDecoyFromTarget(newAAlocations);
                                // If reverse is insufficient, generates decoy through scrambling
                                // If the scrambled decoy is unable to attain sufficient sequence dissimilarity, it defaults to mirroring
                                // Sequence similarity could be any number of methods depending on which gives the best results
                                // For now it is simple percent homology
                                if (SequenceSimilarity(peptide, generatedOnTheFlyDecoy) > 0.3)
                                {
                                    // One problem here is that the SequenceSimilarity score computed above
                                    // is not necessarily the same that GetScrambledDecoyFromTarget uses.
                                    // It is as of now, however this aspect of GetScrambledDecoyFromTarget
                                    // would need to be modified in mzLib if we wanted to experiment with 
                                    // different sequence similarity scores.
                                    generatedOnTheFlyDecoy = peptide.GetScrambledDecoyFromTarget(newAAlocations);
                                }
                                
                            }
                            else if (SpectralLibrary != null)
                            {
                                int[] newAAlocations = new int[peptide.BaseSequence.Length];
                                generatedOnTheFlyDecoy = peptide.GetReverseDecoyFromTarget(newAAlocations);
                            }

                            // clear fragments from the last peptide
                            foreach (var fragmentSet in targetFragmentsForEachDissociationType)
                            {
                                fragmentSet.Value.Clear();
                                decoyFragmentsForEachDissociationType[fragmentSet.Key].Clear();
                            }

                            // score each scan that has an acceptable precursor mass
                            foreach (ScanWithIndexAndNotchInfo scan in GetAcceptableScans(peptide.MonoisotopicMass, SearchMode))
                            {
                                var dissociationType = CommonParameters.DissociationType == DissociationType.Autodetect ?
                                    scan.TheScan.TheScan.DissociationType.Value : CommonParameters.DissociationType;

                                if (!targetFragmentsForEachDissociationType.TryGetValue(dissociationType, out var targetTheorProducts))
                                {
                                    //TODO: print some kind of warning here. the scan header dissociation type was unknown
                                    continue;
                                }
                                // check if we've already generated theoretical fragments for this peptide+dissociation type
                                if (targetTheorProducts.Count == 0)
                                {
                                    peptide.Fragment(dissociationType, CommonParameters.DigestionParams.FragmentationTerminus, targetTheorProducts);
                                }
                                // match theoretical target ions to spectrum
                                List<MatchedFragmentIon> targetMatchedIons = MatchFragmentIons(scan.TheScan, targetTheorProducts, CommonParameters,
                                        matchAllCharges: WriteSpectralLibrary);                                

                                // calculate the peptide's score
                                double targetScore = CalculatePeptideScore(scan.TheScan.TheScan, targetMatchedIons, fragmentsCanHaveDifferentCharges: WriteSpectralLibrary);
                                if (DecoyOnTheFly == true)
                                {
                                    DecoyOnTheFlyComparison(decoyFragmentsForEachDissociationType, dissociationType, generatedOnTheFlyDecoy, scan, targetScore, myLocks, peptide, targetMatchedIons);
                                }
                                else
                                {
                                    AddPeptideCandidateToPsm(scan, myLocks, targetScore, peptide, targetMatchedIons);
                                }
                                
                                if (SpectralLibrary != null)
                                {
                                    DecoyScoreForSpectralLibrarySearch(scan, generatedOnTheFlyDecoy, decoyFragmentsForEachDissociationType, dissociationType, myLocks);
                                }
                            }
                        }

                        // report search progress (proteins searched so far out of total proteins in database)
                        proteinsSearched++;
                        var percentProgress = (int)((proteinsSearched / Proteins.Count) * 100);

                        if (percentProgress > oldPercentProgress)
                        {
                            oldPercentProgress = percentProgress;
                            ReportProgress(new ProgressEventArgs(percentProgress, "Performing classic search... ", NestedIds));
                        }
                    }
                });
            }

            foreach (PeptideSpectralMatch psm in PeptideSpectralMatches.Where(p => p != null))
            {
                psm.ResolveAllAmbiguities();
            }

            return new MetaMorpheusEngineResults(this);
        }

        private double SequenceSimilarity(PeptideWithSetModifications peptide, PeptideWithSetModifications generatedOnTheFlyDecoy)
        {
            double rawScore = 0;
            for (int i = 0; i < peptide.BaseSequence.Length; i++)
            {
                if (peptide.BaseSequence[i] == generatedOnTheFlyDecoy[i])
                {
                    Modification targetMod;
                    if (peptide.AllModsOneIsNterminus.TryGetValue(i + 2, out targetMod))
                    {
                        Modification decoyMod;
                        if (generatedOnTheFlyDecoy.AllModsOneIsNterminus.TryGetValue(i + 2, out decoyMod))
                        {
                            if (decoyMod == targetMod)
                            {
                                rawScore += 1;
                            }
                        }
                    }
                    else
                    {
                        rawScore += 1;
                    }
                }
            }
            return rawScore / peptide.BaseSequence.Length;
        }

        private void DecoyScoreForSpectralLibrarySearch(ScanWithIndexAndNotchInfo scan, PeptideWithSetModifications reversedOnTheFlyDecoy, Dictionary<DissociationType, List<Product>> decoyFragmentsForEachDissociationType, DissociationType dissociationType, object[] myLocks)
        {
            // match decoy ions for decoy-on-the-fly
            var decoyTheoreticalFragments = decoyFragmentsForEachDissociationType[dissociationType];

            if (decoyTheoreticalFragments.Count == 0)
            {
                reversedOnTheFlyDecoy.Fragment(dissociationType, CommonParameters.DigestionParams.FragmentationTerminus, decoyTheoreticalFragments);
            }

            var decoyMatchedIons = MatchFragmentIons(scan.TheScan, decoyTheoreticalFragments, CommonParameters,
                matchAllCharges: WriteSpectralLibrary);

            // calculate decoy's score
            var decoyScore = CalculatePeptideScore(scan.TheScan.TheScan, decoyMatchedIons, fragmentsCanHaveDifferentCharges: WriteSpectralLibrary);

            AddPeptideCandidateToPsm(scan, myLocks, decoyScore, reversedOnTheFlyDecoy, decoyMatchedIons);
        }

        
        // Where decision is made to replace a peptide candidate.
        private void AddPeptideCandidateToPsm(ScanWithIndexAndNotchInfo scan, object[] myLocks, double thisScore, PeptideWithSetModifications peptide, List<MatchedFragmentIon> matchedIons)
        {
            bool meetsScoreCutoff = thisScore >= CommonParameters.ScoreCutoff;

            // this is thread-safe because even if the score improves from another thread writing to this PSM,
            // the lock combined with AddOrReplace method will ensure thread safety
            if (meetsScoreCutoff)
            {
                // valid hit (met the cutoff score); lock the scan to prevent other threads from accessing it
                lock (myLocks[scan.ScanIndex])
                {
                    // Checks score 
                    bool scoreImprovement = PeptideSpectralMatches[scan.ScanIndex] == null || (thisScore - PeptideSpectralMatches[scan.ScanIndex].RunnerUpScore) > -PeptideSpectralMatch.ToleranceForScoreDifferentiation;

                    if (scoreImprovement)
                    {
                        if (PeptideSpectralMatches[scan.ScanIndex] == null)
                        {
                            PeptideSpectralMatches[scan.ScanIndex] = new PeptideSpectralMatch(peptide, scan.Notch, thisScore, scan.ScanIndex, scan.TheScan, CommonParameters, matchedIons, 0);
                        }
                        else
                        {
                            PeptideSpectralMatches[scan.ScanIndex].AddOrReplace(peptide, thisScore, scan.Notch, CommonParameters.ReportAllAmbiguity, matchedIons, 0);
                        }
                    }
                }
            }
        }

        private IEnumerable<ScanWithIndexAndNotchInfo> GetAcceptableScans(double peptideMonoisotopicMass, MassDiffAcceptor searchMode)
        {
            foreach (AllowedIntervalWithNotch allowedIntervalWithNotch in searchMode.GetAllowedPrecursorMassIntervalsFromTheoreticalMass(peptideMonoisotopicMass).ToList())
            {
                DoubleRange allowedInterval = allowedIntervalWithNotch.AllowedInterval;
                int scanIndex = GetFirstScanWithMassOverOrEqual(allowedInterval.Minimum);
                if (scanIndex < ArrayOfSortedMS2Scans.Length)
                {
                    var scanMass = MyScanPrecursorMasses[scanIndex];
                    while (scanMass <= allowedInterval.Maximum)
                    {
                        var scan = ArrayOfSortedMS2Scans[scanIndex];
                        yield return new ScanWithIndexAndNotchInfo(scan, allowedIntervalWithNotch.Notch, scanIndex);
                        scanIndex++;
                        if (scanIndex == ArrayOfSortedMS2Scans.Length)
                        {
                            break;
                        }

                        scanMass = MyScanPrecursorMasses[scanIndex];
                    }
                }
            }
        }

        private int GetFirstScanWithMassOverOrEqual(double minimum)
        {
            int index = Array.BinarySearch(MyScanPrecursorMasses, minimum);
            if (index < 0)
            {
                index = ~index;
            }
            // index of the first element that is larger than value
            return index;
        }
        /// <summary>
        /// Method to handle spectral comparison for Decoy on the fly
        /// Calculates theoretical fragments for the decoy an matches them to a given scan
        /// Also performes score comparison between the decoy and target to select which one to add into PSMs
        /// </summary>
        /// <param name="decoyFragmentsForEachDissociationType">Dictionary containing product ion lists for each dissociation type for the decoy</param>
        /// <param name="dissociationType">DissociationType being used</param>
        /// <param name="generatedOnTheFlyDecoy">Decoy PeptideWithSetModifications generated if DOTF is being used</param>
        /// <param name="scan"></param>
        /// <param name="targetScore"></param>
        /// <param name="myLocks"></param>
        /// <param name="peptide">Scan to match to</param>
        /// <param name="targetMatchedIons">List<MatchedFragmentIon> containing ions matched to the target peptide</param>
        private void DecoyOnTheFlyComparison(Dictionary<DissociationType, List<Product>> decoyFragmentsForEachDissociationType, DissociationType dissociationType, 
            PeptideWithSetModifications generatedOnTheFlyDecoy, ScanWithIndexAndNotchInfo scan, double targetScore, object[] myLocks, PeptideWithSetModifications peptide, 
            List<MatchedFragmentIon> targetMatchedIons)
        {
            decoyFragmentsForEachDissociationType.TryGetValue(dissociationType, out var decoyTheorProducts);
            if (decoyTheorProducts.Count == 0)
            {
                generatedOnTheFlyDecoy.Fragment(dissociationType, CommonParameters.DigestionParams.FragmentationTerminus, decoyTheorProducts);
            }
            List<MatchedFragmentIon> decoyMatchedIons = MatchFragmentIons(scan.TheScan, decoyTheorProducts, CommonParameters,
                matchAllCharges: WriteSpectralLibrary);
            double decoyScore = CalculatePeptideScore(scan.TheScan.TheScan, decoyMatchedIons, fragmentsCanHaveDifferentCharges: WriteSpectralLibrary);
            if (decoyScore > targetScore + PeptideSpectralMatch.ToleranceForScoreDifferentiation)
            {
                AddPeptideCandidateToPsm(scan, myLocks, (double)decoyScore, generatedOnTheFlyDecoy, decoyMatchedIons);
            }
            // Figure out how to get a tolerance but this is the general scheme.
            else if (Math.Abs(decoyScore - targetScore) < PeptideSpectralMatch.ToleranceForScoreDifferentiation)
            {
                AddPeptideCandidateToPsm(scan, myLocks, (double)decoyScore, generatedOnTheFlyDecoy, decoyMatchedIons);
                AddPeptideCandidateToPsm(scan, myLocks, targetScore, peptide, targetMatchedIons);
            }
            else
            {
                AddPeptideCandidateToPsm(scan, myLocks, targetScore, peptide, targetMatchedIons);
            }
        }
    }
}