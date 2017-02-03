﻿using EngineLayer;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskLayer;

namespace MetaMorpheusGUI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        #region Private Fields

        private readonly ObservableCollection<RawDataForDataGrid> rawDataObservableCollection = new ObservableCollection<RawDataForDataGrid>();
        private readonly ObservableCollection<ProteinDbForDataGrid> proteinDbObservableCollection = new ObservableCollection<ProteinDbForDataGrid>();
        private readonly ObservableCollection<FinishedFileForDataGrid> finishedFileObservableCollection = new ObservableCollection<FinishedFileForDataGrid>();
        private readonly ObservableCollection<MyTaskEngine> taskEngineObservableCollection = new ObservableCollection<MyTaskEngine>();

        #endregion Private Fields

        #region Public Constructors

        public MainWindow()
        {
            InitializeComponent();

            if (MyEngine.MetaMorpheusVersion.Equals("1.0.0.0"))
                Title = "MetaMorpheus: Not a release version";
            else
                Title = "MetaMorpheus: version " + MyEngine.MetaMorpheusVersion;

            dataGridXMLs.DataContext = proteinDbObservableCollection;
            dataGridDatafiles.DataContext = rawDataObservableCollection;
            tasksDataGrid.DataContext = taskEngineObservableCollection;
            outputFilesDataGrid.DataContext = finishedFileObservableCollection;

            //proteinDbObservableCollection.Add(new XMLdb(@"C:\Users\stepa\Data\CalibrationPaperData\OrigData\uniprot-mouse-reviewed-1-23-2017.xml"));
            //proteinDbObservableCollection.Add(new XMLdb(@"C:\Users\stepa\Data\CalibrationPaperData\OrigData\uniprot-human-reviewed-1-23-2017.xml"));

            //rawDataObservableCollection.Add(new RawData(@"C:\Users\stepa\Downloads\small.pwiz.1.1.mzML"));

            //rawDataObservableCollection.Add(new RawData(@"C:\Users\stepa\Data\CalibrationPaperData\OrigData\Mouse\2017-01-30-19-07-49\Task1Calibrate\04-30-13_CAST_Frac5_4uL-Calibrated.mzML"));

            EverythingRunnerEngine.newDbsHandler += AddNewDB;
            EverythingRunnerEngine.newSpectrasHandler += AddNewSpectra;

            EverythingRunnerEngine.startingAllTasksEngineHandler += NewSuccessfullyStartingAllTasks;
            EverythingRunnerEngine.finishedAllTasksEngineHandler += NewSuccessfullyFinishedAllTasks;

            MyTaskEngine.StartingSingleTaskHander += Po_startingSingleTaskHander;
            MyTaskEngine.FinishedSingleTaskHandler += Po_finishedSingleTaskHandler;
            MyTaskEngine.FinishedWritingFileHandler += NewSuccessfullyFinishedWritingFile;
            MyTaskEngine.StartingDataFileHandler += MyTaskEngine_StartingDataFileHandler;
            MyTaskEngine.FinishedDataFileHandler += MyTaskEngine_FinishedDataFileHandler;
            MyEngine.OutProgressHandler += NewoutProgressBar;
            MyEngine.OutLabelStatusHandler += NewoutLabelStatus;
            MyEngine.StartingSingleEngineHander += MyEngine_startingSingleEngineHander;
            MyEngine.FinishedSingleEngineHandler += MyEngine_finishedSingleEngineHandler;

            UpdateTaskGuiStuff();
        }

        #endregion Public Constructors

        #region Private Methods

        private void MyTaskEngine_FinishedDataFileHandler(object sender, StringEventArgs s)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => MyTaskEngine_FinishedDataFileHandler(sender, s)));
            }
            else
            {
                var huh = rawDataObservableCollection.First(b => b.FileName.Equals(s.s));
                huh.SetInProgress(false);
                dataGridDatafiles.Items.Refresh();
            }
        }

        private void MyTaskEngine_StartingDataFileHandler(object sender, StringEventArgs s)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => MyTaskEngine_StartingDataFileHandler(sender, s)));
            }
            else
            {
                var huh = rawDataObservableCollection.First(b => b.FileName.Equals(s.s));
                huh.SetInProgress(true);
                dataGridDatafiles.Items.Refresh();
            }
        }

        private void MyEngine_startingSingleEngineHander(object sender, SingleEngineEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => MyEngine_startingSingleEngineHander(sender, e)));
            }
            else
            {
                statusLabel.Content = "Running " + e.myEngine.GetType().Name + " engine...";
                outProgressBar.IsIndeterminate = true;
            }
        }

        private void MyEngine_finishedSingleEngineHandler(object sender, SingleEngineFinishedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => MyEngine_finishedSingleEngineHandler(sender, e)));
            }
            else
            {
                outRichTextBox.AppendText(e.ToString() + Environment.NewLine);
                outRichTextBox.ScrollToEnd();
            }
        }

        private void AddNewDB(object sender, XmlForTaskListEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AddNewDB(sender, e)));
            }
            else
            {
                foreach (var uu in proteinDbObservableCollection)
                    uu.Use = false;
                foreach (var uu in e.newDatabases)
                    proteinDbObservableCollection.Add(new ProteinDbForDataGrid(uu.FileName));
            }
        }

        private void AddNewSpectra(object sender, StringListEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AddNewSpectra(sender, e)));
            }
            else
            {
                foreach (var uu in rawDataObservableCollection)
                    uu.Use = false;
                foreach (var newRawData in e.StringList)
                    rawDataObservableCollection.Add(new RawDataForDataGrid(newRawData));
            }
        }

        private void Po_startingSingleTaskHander(object sender, SingleTaskEventArgs s)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Po_startingSingleTaskHander(sender, s)));
            }
            else
            {
                s.TheTask.IsMySelected = true;
                statusLabel.Content = "Running " + s.TheTask.TaskType + " task";
                outProgressBar.IsIndeterminate = true;

                tasksDataGrid.Items.Refresh();
                dataGridDatafiles.Items.Refresh();
                dataGridXMLs.Items.Refresh();
            }
        }

        private void Po_finishedSingleTaskHandler(object sender, SingleTaskEventArgs s)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => Po_finishedSingleTaskHandler(sender, s)));
            }
            else
            {
                s.TheTask.IsMySelected = false;
                statusLabel.Content = "Finished " + s.TheTask.TaskType + " task";
                outProgressBar.Value = 100;

                tasksDataGrid.Items.Refresh();
                dataGridDatafiles.Items.Refresh();
                dataGridXMLs.Items.Refresh();
            }
        }

        private void AddFinishedFile(string filepath)
        {
            finishedFileObservableCollection.Add(new FinishedFileForDataGrid(filepath));
            outputFilesDataGrid.Items.Refresh();
        }

        private void ClearRaw_Click(object sender, RoutedEventArgs e)
        {
            rawDataObservableCollection.Clear();
        }

        private void AddXML_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openPicker = new Microsoft.Win32.OpenFileDialog();
            openPicker.Filter = "Database Files|*.xml;*.xml.gz;*.fasta";
            openPicker.FilterIndex = 1;
            openPicker.RestoreDirectory = true;
            openPicker.Multiselect = true;

            if (openPicker.ShowDialog() == true)
                foreach (var filepath in openPicker.FileNames)
                    proteinDbObservableCollection.Add(new ProteinDbForDataGrid(filepath));
            dataGridXMLs.Items.Refresh();
        }

        private void AddRaw_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog openFileDialog1 = new Microsoft.Win32.OpenFileDialog();
            openFileDialog1.Filter = "Spectra Files(*.raw;*.mzML)|*.raw;*.mzML";
            openFileDialog1.FilterIndex = 1;
            openFileDialog1.RestoreDirectory = true;
            openFileDialog1.Multiselect = true;

            if (openFileDialog1.ShowDialog() == true)
                foreach (var rawDataFromSelected in openFileDialog1.FileNames)
                    rawDataObservableCollection.Add(new RawDataForDataGrid(rawDataFromSelected));
            dataGridDatafiles.Items.Refresh();
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var rawDataFromDragged in files)
            {
                var theExtension = Path.GetExtension(rawDataFromDragged).ToLowerInvariant();
                switch (theExtension)
                {
                    case ".raw":
                    case ".mzml":
                        rawDataObservableCollection.Add(new RawDataForDataGrid(rawDataFromDragged));
                        break;

                    case ".xml":
                    case ".fasta":
                    case ".gz":
                        proteinDbObservableCollection.Add(new ProteinDbForDataGrid(rawDataFromDragged));
                        break;
                }
                dataGridDatafiles.Items.Refresh();
            }
        }

        private void Row_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            var ye = sender as DataGridCell;
            var hm = ye.Content as TextBlock;
            if (hm != null && !string.IsNullOrEmpty(hm.Text))
            {
                System.Diagnostics.Process.Start(hm.Text);
            }
        }

        private void RunAllTasks_Click(object sender, RoutedEventArgs e)
        {
            EverythingRunnerEngine a = new EverythingRunnerEngine(taskEngineObservableCollection.ToList(), rawDataObservableCollection.Where(b => b.Use).Select(b => b.FileName).ToList(), proteinDbObservableCollection.Where(b => b.Use).Select(b => new DbForTask(b.FileName, b.Contaminant)).ToList());
            var t = new Thread(() => a.Run());
            t.IsBackground = true;
            t.Start();
        }

        private void ClearTasks_Click(object sender, RoutedEventArgs e)
        {
            taskEngineObservableCollection.Clear();
            UpdateTaskGuiStuff();
        }

        private void UpdateTaskGuiStuff()
        {
            if (taskEngineObservableCollection.Count == 0)
            {
                RunTasksButton.IsEnabled = false;
                RemoveLastTaskButton.IsEnabled = false;
                ClearTasksButton.IsEnabled = false;
            }
            else
            {
                RunTasksButton.IsEnabled = true;
                RemoveLastTaskButton.IsEnabled = true;
                ClearTasksButton.IsEnabled = true;
            }
        }

        private void addSearchTaskButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SearchTaskWindow();
            if (dialog.ShowDialog() == true)
            {
                taskEngineObservableCollection.Add(dialog.TheTask);
                UpdateTaskGuiStuff();
            }
        }

        private void addCalibrateTaskButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CalibrateTaskWindow();
            if (dialog.ShowDialog() == true)
            {
                taskEngineObservableCollection.Add(dialog.TheTask);
                UpdateTaskGuiStuff();
            }
        }

        private void addGPTMDTaskButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new GPTMDTaskWindow();
            if (dialog.ShowDialog() == true)
            {
                taskEngineObservableCollection.Add(dialog.TheTask);
                UpdateTaskGuiStuff();
            }
        }

        private void RemoveLastTask_Click(object sender, RoutedEventArgs e)
        {
            taskEngineObservableCollection.RemoveAt(taskEngineObservableCollection.Count - 1);
            UpdateTaskGuiStuff();
        }

        private void tasksDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var a = sender as DataGrid;
            var ok = (MyTaskEngine)a.SelectedItem;
            if (ok != null)
                switch (ok.TaskType)
                {
                    case MyTask.Search:
                        var searchDialog = new SearchTaskWindow(ok as SearchTask);
                        searchDialog.ShowDialog();
                        break;

                    case MyTask.Gptmd:
                        var gptmddialog = new GPTMDTaskWindow(ok as GptmdTask);
                        gptmddialog.ShowDialog();
                        break;

                    case MyTask.Calibrate:
                        var calibratedialog = new CalibrateTaskWindow(ok as CalibrationTask);
                        calibratedialog.ShowDialog();
                        break;
                }
        }

        private void NewoutLabelStatus(object sender, StringEventArgs s)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => NewoutLabelStatus(sender, s)));
            }
            else
            {
                outProgressBar.IsIndeterminate = true;
                statusLabel.Content = s.s;
            }
        }

        private void NewoutProgressBar(object sender, ProgressEventArgs s)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => NewoutProgressBar(sender, s)));
            }
            else
            {
                outProgressBar.IsIndeterminate = false;
                outProgressBar.Value = s.new_progress;
                statusLabel.Content = s.v;
            }
        }

        private void NewRefreshBetweenTasks(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => NewRefreshBetweenTasks(sender, e)));
            }
            else
            {
                dataGridDatafiles.Items.Refresh();
                dataGridXMLs.Items.Refresh();
            }
        }

        private void NewSuccessfullyStartingAllTasks(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => NewSuccessfullyStartingAllTasks(sender, e)));
            }
            else
            {
                //TODO: Check those
                XMLdbPanel.IsEnabled = false;
                DatafilesStackPanel.IsEnabled = false;
                addSearchTaskButton.IsEnabled = false;
                addCalibrateTaskButton.IsEnabled = false;
                addGPTMDTaskButton.IsEnabled = false;
                tasksPanel.IsEnabled = false;

                statusLabel.Content = "Starting all tasks...";
                outProgressBar.IsIndeterminate = true;

                dataGridDatafiles.Items.Refresh();
            }
        }

        private void NewSuccessfullyFinishedAllTasks(object sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => NewSuccessfullyFinishedAllTasks(sender, e)));
            }
            else
            {
                //TODO: Check those
                XMLdbPanel.IsEnabled = true;
                DatafilesStackPanel.IsEnabled = true;
                addSearchTaskButton.IsEnabled = true;
                addCalibrateTaskButton.IsEnabled = true;
                addGPTMDTaskButton.IsEnabled = true;
                tasksPanel.IsEnabled = true;

                statusLabel.Content = "Finished all tasks!";
                outProgressBar.IsIndeterminate = false;
                outProgressBar.Value = 100;

                dataGridDatafiles.Items.Refresh();
            }
        }

        private void NewSuccessfullyFinishedWritingFile(object sender, SingleFileEventArgs v)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => NewSuccessfullyFinishedWritingFile(sender, v)));
            }
            else
            {
                AddFinishedFile(v.writtenFile);
            }
        }

        private void ClearXML_Click(object sender, RoutedEventArgs e)
        {
            proteinDbObservableCollection.Clear();
        }

        private void ClearOutput_Click(object sender, RoutedEventArgs e)
        {
            finishedFileObservableCollection.Clear();
        }

        #endregion Private Methods

    }
}