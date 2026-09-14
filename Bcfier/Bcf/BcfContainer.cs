using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.Data.Utils;
using Bcfier.Localization;

namespace Bcfier.Bcf
{
    /// <summary>
    /// Model View: вкладки BCF-файлов и операции open/save.
    /// </summary>
    public class BcfContainer : INotifyPropertyChanged
    {
        private ObservableCollection<BcfFile> _bcfFiles;
        private int selectedReport;

        public BcfContainer()
        {
            BcfFiles = new ObservableCollection<BcfFile>();
            Globals.LoadFromUserSettings();
        }

        public ObservableCollection<BcfFile> BcfFiles
        {
            get => _bcfFiles;
            set
            {
                _bcfFiles = value;
                NotifyPropertyChanged(nameof(BcfFiles));
            }
        }

        public int SelectedReportIndex
        {
            get => selectedReport;
            set
            {
                selectedReport = value;
                NotifyPropertyChanged(nameof(SelectedReportIndex));
            }
        }

        public void NewFile()
        {
            BcfFiles.Add(new BcfFile());
            SelectedReportIndex = BcfFiles.Count - 1;
        }

        public void SaveFile(BcfFile bcf) => SaveBcfFile(bcf);

        public void MergeFiles(BcfFile bcf)
        {
            var bcffiles = OpenBcfDialog();
            if (bcffiles == null)
                return;

            bcf.MergeBcfFile(bcffiles);
        }

        public void OpenFile(string path) => BcfOpened(BcfReader.Open(path));

        /// <summary>
        /// Добавляет уже прочитанный BCF-файл в контейнер на UI-потоке.
        /// </summary>
        public void AddOpenedFile(BcfFile bcf) => BcfOpened(bcf);

        public void OpenFile()
        {
            var bcffiles = OpenBcfDialog();
            if (bcffiles == null)
                return;

            foreach (var bcffile in bcffiles)
            {
                if (bcffile != null)
                    BcfOpened(bcffile);
            }
        }

        private void BcfOpened(BcfFile newbcf)
        {
            if (newbcf == null)
                return;

            BcfFiles.Add(newbcf);
            SelectedReportIndex = BcfFiles.Count - 1;
            if (newbcf.Issues.Any())
                newbcf.SelectedIssue = newbcf.Issues.First();

            foreach (var issue in newbcf.Issues)
            {
                issue.RegisterEvents();

                // Labels[] → SelectedLabels для UI; Open* пополняем из файла
                BcfIssueHelper.SyncLabelsFromTopic(issue.Topic);

                AddOpenValue(Globals.OpenStatuses, issue.Topic.TopicStatus);
                AddOpenValue(Globals.OpenTypes, issue.Topic.TopicType);
                AddOpenValue(Globals.OpenPriorities, issue.Topic.Priority);
                AddOpenValue(Globals.OpenAssignees, issue.Topic.AssignedTo);
                if (issue.Topic.Labels != null)
                {
                    foreach (string label in issue.Topic.Labels)
                        AddOpenValue(Globals.OpenLabels, label);
                }
            }
        }

        public void CloseFile(BcfFile bcf)
        {
            try
            {
                _bcfFiles.Remove(bcf);
                Utils.DeleteDirectory(bcf.TempPath);
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }
        }

        /// <summary>
        /// Обновляет списки статусов/типов/приоритетов/меток в открытых issue.
        /// Коллекции мутируем на месте: ComboBox уже привязан, замена объекта его не обновляет.
        /// </summary>
        public void UpdateDropdowns()
        {
            try
            {
                Globals.LoadFromUserSettings();

                foreach (var bcf in BcfFiles)
                {
                    foreach (var issue in bcf.Issues)
                    {
                        if (issue?.Topic == null)
                            continue;

                        string selectedLabel = issue.Topic.SelectedLabels?.FirstOrDefault()
                            ?? issue.Topic.Labels?.FirstOrDefault();

                        RefreshCollection(issue.Topic.TopicStatusesCollection, Globals.AvailStatuses, issue.Topic.TopicStatus);
                        RefreshCollection(issue.Topic.TopicTypesCollection, Globals.AvailTypes, issue.Topic.TopicType);
                        RefreshCollection(issue.Topic.PrioritiesCollection, Globals.AvailPriorities, issue.Topic.Priority);
                        RefreshCollection(issue.Topic.LabelsCollection, Globals.AvailLabels, selectedLabel);
                        RefreshCollection(issue.Topic.AssigneesCollection, Globals.AvailAssignees, issue.Topic.AssignedTo);
                        issue.Topic.NotifyDropdownCollectionsChanged();
                    }
                }
            }
            catch
            {
                // подавляем ошибки настроек
            }
        }

        /// <summary>
        /// Добавляет значение из открытого BCF в глобальный список, если его ещё нет.
        /// </summary>
        private static void AddOpenValue(List<string> target, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(value) || target.Contains(value))
                return;

            target.Add(value);
        }

        private static void RefreshCollection(ObservableCollection<string> target, IEnumerable<string> source, string selected)
        {
            if (target == null)
                return;

            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (target[i] != selected)
                    target.RemoveAt(i);
            }

            foreach (var item in source ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(item))
                    continue;

                if (item != selected || !target.Contains(item))
                    target.Add(item);
            }
        }

        private static IEnumerable<BcfFile> OpenBcfDialog()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = Loc.Get("OpenBcfFilter"),
                    DefaultExt = ".bcf",
                    Multiselect = true,
                    RestoreDirectory = true,
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                if (dialog.ShowDialog() == true)
                    return dialog.FileNames.Select(BcfReader.Open).ToList();
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }

            return null;
        }

        private static bool SaveBcfFile(BcfFile bcffile)
        {
            try
            {
                if (bcffile.Issues.Count == 0)
                {
                    MessageBox.Show(Loc.Get("EmptyBcf"), Loc.Get("NoIssue"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Перед записью UI-метки → Labels[]
                foreach (var issue in bcffile.Issues)
                    BcfIssueHelper.SyncLabelsToTopic(issue.Topic);

                string name = !string.IsNullOrEmpty(bcffile.Filename) ? bcffile.Filename : Loc.Get("NewBcfReport");
                string filename = SaveBcfDialog(name);
                if (string.IsNullOrWhiteSpace(filename))
                    return false;

                if (!BcfWriter.Save(bcffile, filename))
                    return false;

                if (File.Exists(filename))
                    System.Diagnostics.Process.Start("explorer.exe", @"/select, " + filename);

                return true;
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }

            return false;
        }

        private static string SaveBcfDialog(string filename)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Loc.Get("SaveBcf"),
                FileName = filename,
                DefaultExt = ".bcf",
                Filter = Loc.Get("OpenBcfFilter")
            };

            return saveFileDialog.ShowDialog() == true ? saveFileDialog.FileName : string.Empty;
        }

        [field: NonSerialized]
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(string info)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(info));
        }
    }
}
