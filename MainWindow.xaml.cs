using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace PhotoFileManager;

public partial class MainWindow : Window
{
    private static readonly HashSet<string> JpegExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg"
    };

    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr3", ".cr2", ".nef", ".arw", ".dng"
    };

    private static readonly string SettingsFilePath = Path.Combine(
        AppContext.BaseDirectory,
        "folder-settings.json");

    private readonly ObservableCollection<ReportRow> _reportRows = new();
    private readonly ICollectionView _reportView;
    private string _lastSummaryText = string.Empty;
    private bool _isLoadingSettings;

    public MainWindow()
    {
        InitializeComponent();
        _reportView = CollectionViewSource.GetDefaultView(_reportRows);
        _reportView.Filter = FilterReportRow;
        GridReport.ItemsSource = _reportView;
        Loaded += MainWindow_Loaded;
    }

    private bool FilterReportRow(object item)
    {
        if (item is not ReportRow row) return false;
        if (RbFilterMissing?.IsChecked == true)
        {
            return row.Status == "Không tìm thấy";
        }
        if (RbFilterDuplicate?.IsChecked == true)
        {
            return row.HasDuplicate;
        }
        return true;
    }

    private void ReportFilter_Changed(object sender, RoutedEventArgs e)
    {
        _reportView?.Refresh();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        LoadFolderSettings();
    }

    private void LoadFolderSettings()
    {
        _isLoadingSettings = true;
        try
        {
            if (!File.Exists(SettingsFilePath)) return;

            string json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<FolderSettings>(json);
            if (settings == null) return;

            TxtJpegSource.Text = settings.JpegSource ?? string.Empty;
            TxtRawSource.Text = settings.RawSource ?? string.Empty;
            TxtJpegDest.Text = settings.JpegDest ?? string.Empty;
            TxtRawDest.Text = settings.RawDest ?? string.Empty;
        }
        catch
        {
            // Bỏ qua nếu file cấu hình hỏng hoặc chưa thể đọc; người dùng vẫn nhập bình thường.
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void SaveFolderSettings()
    {
        if (_isLoadingSettings) return;

        try
        {
            var settings = new FolderSettings
            {
                JpegSource = TxtJpegSource.Text.Trim(),
                RawSource = TxtRawSource.Text.Trim(),
                JpegDest = TxtJpegDest.Text.Trim(),
                RawDest = TxtRawDest.Text.Trim()
            };

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);
        }
        catch
        {
            // Không làm gián đoạn giao diện nếu thư mục chạy không cho phép ghi file cấu hình.
        }
    }

    private void FolderPath_TextChanged(object sender, TextChangedEventArgs e)
    {
        SaveFolderSettings();
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string target) return;

        var dialog = new OpenFolderDialog
        {
            Title = "Chọn thư mục"
        };

        if (dialog.ShowDialog() == true)
        {
            switch (target)
            {
                case "JpegSource": TxtJpegSource.Text = dialog.FolderName; break;
                case "RawSource": TxtRawSource.Text = dialog.FolderName; break;
                case "JpegDest": TxtJpegDest.Text = dialog.FolderName; break;
                case "RawDest": TxtRawDest.Text = dialog.FolderName; break;
            }
            SaveFolderSettings();
        }
    }

    private void BtnLoadTxt_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            TxtImageNames.Text = File.ReadAllText(dlg.FileName, Encoding.UTF8);
            MainTabControl.SelectedIndex = 1;
        }
    }

    private void BtnPasteClipboard_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            TxtImageNames.Text = Clipboard.GetText();
            MainTabControl.SelectedIndex = 1;
        }
    }

    private void BtnClearList_Click(object sender, RoutedEventArgs e)
    {
        TxtImageNames.Clear();
    }

    private async void BtnRun_Click(object sender, RoutedEventArgs e)
    {
        int modeIndex = MainTabControl.SelectedIndex;
        string jpegSource = TxtJpegSource.Text.Trim();
        string rawSource = TxtRawSource.Text.Trim();
        string jpegDest = TxtJpegDest.Text.Trim();
        string rawDest = TxtRawDest.Text.Trim();

        bool needJpegSource = (modeIndex == 0 && RbJpegToRaw.IsChecked == true) || modeIndex == 1;
        bool needRawSource = (modeIndex == 0 && RbRawToJpeg.IsChecked == true) || modeIndex == 1;
        bool needJpegDest = (modeIndex == 0 && RbRawToJpeg.IsChecked == true) || modeIndex == 1;
        bool needRawDest = (modeIndex == 0 && RbJpegToRaw.IsChecked == true) || modeIndex == 1;

        if (needJpegSource && !Directory.Exists(jpegSource))
        {
            MessageBox.Show("Vui lòng chọn thư mục JPEG nguồn hợp lệ.", "Thiếu thư mục", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (needRawSource && !Directory.Exists(rawSource))
        {
            MessageBox.Show("Vui lòng chọn thư mục RAW nguồn hợp lệ.", "Thiếu thư mục", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (needJpegDest && string.IsNullOrWhiteSpace(jpegDest))
        {
            MessageBox.Show("Vui lòng chọn thư mục JPEG đích.", "Thiếu thư mục", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (needRawDest && string.IsNullOrWhiteSpace(rawDest))
        {
            MessageBox.Show("Vui lòng chọn thư mục RAW đích.", "Thiếu thư mục", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        List<string> targetNames;
        if (modeIndex == 0)
        {
            string seedFolder = RbJpegToRaw.IsChecked == true ? jpegSource : rawSource;
            HashSet<string> allowedExts = RbJpegToRaw.IsChecked == true ? JpegExtensions : RawExtensions;
            targetNames = ExtractBaseNamesFromFolder(seedFolder, allowedExts);
        }
        else if (modeIndex == 1)
        {
            targetNames = ParseNamesFromText(TxtImageNames.Text);
        }
        else
        {
            MessageBox.Show("Hãy chọn Chế độ 1 (JPEG ↔ RAW) hoặc Chế độ 2 (TEXT → JPEG + RAW) để chạy.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (targetNames.Count == 0)
        {
            MessageBox.Show("Không tìm thấy tên ảnh nào để xử lý.", "Danh sách trống", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetBusy(true);
        _reportRows.Clear();
        ProgressBarMain.Maximum = targetNames.Count;
        ProgressBarMain.Value = 0;

        int copiedCount = 0;
        int missingCount = 0;
        int skippedCount = 0;
        int duplicateNameCount = 0;

        bool isJpegToRawSelected = RbJpegToRaw.IsChecked == true;
        bool isRawToJpegSelected = RbRawToJpeg.IsChecked == true;
        bool shouldOpenDest = ChkOpenDestAfterDone.IsChecked == true;

        try
        {
            await Task.Run(() =>
            {
                bool runJpegToRaw = (modeIndex == 0 && isJpegToRawSelected) || modeIndex == 1;
                bool runRawToJpeg = (modeIndex == 0 && isRawToJpegSelected) || modeIndex == 1;

                Dictionary<string, List<string>>? jpegIndex = runRawToJpeg
                    ? IndexFilesByBaseName(jpegSource, JpegExtensions)
                    : null;

                Dictionary<string, List<string>>? rawIndex = runJpegToRaw
                    ? IndexFilesByBaseName(rawSource, RawExtensions)
                    : null;

                if (runRawToJpeg && !string.IsNullOrWhiteSpace(jpegDest))
                    Directory.CreateDirectory(jpegDest);
                if (runJpegToRaw && !string.IsNullOrWhiteSpace(rawDest))
                    Directory.CreateDirectory(rawDest);

                int processed = 0;
                foreach (string baseName in targetNames)
                {
                    if (runJpegToRaw && rawIndex != null)
                    {
                        var row = ProcessOneTarget(
                            baseName,
                            "RAW",
                            rawIndex,
                            rawDest,
                            ref copiedCount,
                            ref missingCount,
                            ref skippedCount,
                            ref duplicateNameCount);
                        Dispatcher.Invoke(() => _reportRows.Add(row));
                    }

                    if (runRawToJpeg && jpegIndex != null)
                    {
                        var row = ProcessOneTarget(
                            baseName,
                            "JPEG",
                            jpegIndex,
                            jpegDest,
                            ref copiedCount,
                            ref missingCount,
                            ref skippedCount,
                            ref duplicateNameCount);
                        Dispatcher.Invoke(() => _reportRows.Add(row));
                    }

                    processed++;
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBarMain.Value = processed;
                        LblStatus.Text = $"Đang xử lý {processed}/{targetNames.Count}: {baseName}";
                    });
                }
            });

            int totalRequests = modeIndex == 1 ? targetNames.Count * 2 : targetNames.Count;
            LblTotal.Text = totalRequests.ToString();
            LblCopied.Text = copiedCount.ToString();
            LblMissing.Text = missingCount.ToString();
            LblSkipped.Text = skippedCount.ToString();

            _lastSummaryText =
                $"Tổng yêu cầu: {totalRequests} | Đã copy: {copiedCount} | Không tìm thấy: {missingCount} | Bỏ qua (đã tồn tại): {skippedCount} | Trùng tên ở thư mục con: {duplicateNameCount}";
            LblStatus.Text = "Hoàn tất. " + _lastSummaryText;
            BtnExportReport.IsEnabled = _reportRows.Count > 0;
            _reportView.Refresh();

            if (shouldOpenDest && copiedCount > 0)
            {
                OpenDestinationFolders(modeIndex, isJpegToRawSelected, isRawToJpegSelected, jpegDest, rawDest);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi trong quá trình xử lý: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            LblStatus.Text = "Đã xảy ra lỗi.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static void OpenDestinationFolders(
        int modeIndex,
        bool isJpegToRawSelected,
        bool isRawToJpegSelected,
        string jpegDest,
        string rawDest)
    {
        bool runJpegToRaw = (modeIndex == 0 && isJpegToRawSelected) || modeIndex == 1;
        bool runRawToJpeg = (modeIndex == 0 && isRawToJpegSelected) || modeIndex == 1;

        var foldersToOpen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (runJpegToRaw && !string.IsNullOrWhiteSpace(rawDest)) foldersToOpen.Add(rawDest);
        if (runRawToJpeg && !string.IsNullOrWhiteSpace(jpegDest)) foldersToOpen.Add(jpegDest);

        foreach (string folder in foldersToOpen)
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folder}\"",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Bỏ qua nếu không mở được cửa sổ Explorer.
            }
        }
    }

    private static ReportRow ProcessOneTarget(
        string baseName,
        string kind,
        Dictionary<string, List<string>> sourceIndex,
        string destFolder,
        ref int copiedCount,
        ref int missingCount,
        ref int skippedCount,
        ref int duplicateNameCount)
    {
        if (!sourceIndex.TryGetValue(baseName, out List<string>? matchedFiles) || matchedFiles.Count == 0)
        {
            missingCount++;
            return new ReportRow(baseName, kind, "Không tìm thấy", $"Không có file {kind} khớp tên", false);
        }

        string sourceFilePath = matchedFiles[0];
        bool hasDuplicate = matchedFiles.Count > 1;
        string duplicateNote = string.Empty;
        if (hasDuplicate)
        {
            duplicateNameCount++;
            string otherPaths = string.Join("; ", matchedFiles.Skip(1));
            duplicateNote = $" | [CẢNH BÁO: Có {matchedFiles.Count} file trùng tên, đã lấy file đầu. Các bản khác: {otherPaths}]";
        }

        string destFilePath = Path.Combine(destFolder, Path.GetFileName(sourceFilePath));
        if (File.Exists(destFilePath))
        {
            skippedCount++;
            return new ReportRow(baseName, kind, "Bỏ qua (đã tồn tại)", $"{destFilePath}{duplicateNote}", hasDuplicate);
        }

        File.Copy(sourceFilePath, destFilePath, overwrite: false);
        copiedCount++;
        return new ReportRow(baseName, kind, "Đã copy", $"{sourceFilePath} -> {destFilePath}{duplicateNote}", hasDuplicate);
    }

    private static List<string> ExtractBaseNamesFromFolder(string folderPath, HashSet<string> allowedExtensions)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (string file in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (!allowedExtensions.Contains(ext)) continue;

            string nameWithoutExt = Path.GetFileNameWithoutExtension(file).Trim();
            if (nameWithoutExt.Length > 0 && seen.Add(nameWithoutExt))
            {
                result.Add(nameWithoutExt);
            }
        }

        return result;
    }

    private static List<string> ParseNamesFromText(string rawText)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        var lines = rawText.Split(new[] { "\r\n", "\n", "\r", "\t", ";" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string line in lines)
        {
            string trimmed = line.Trim().Trim('"');
            if (trimmed.Length == 0) continue;

            string baseName = Path.GetFileNameWithoutExtension(trimmed).Trim();
            if (baseName.Length > 0 && seen.Add(baseName))
            {
                result.Add(baseName);
            }
        }

        return result;
    }

    private static Dictionary<string, List<string>> IndexFilesByBaseName(string folderPath, HashSet<string> allowedExtensions)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (!allowedExtensions.Contains(ext)) continue;

            string baseName = Path.GetFileNameWithoutExtension(file).Trim();
            if (baseName.Length == 0) continue;

            if (!index.TryGetValue(baseName, out List<string>? list))
            {
                list = new List<string>();
                index[baseName] = list;
            }
            list.Add(file);
        }

        return index;
    }

    private void BtnExportReport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            FileName = $"BaoCao_PhotoFileManager_{DateTime.Now:yyyyMMdd_HHmmss}",
            Filter = "CSV file (*.csv)|*.csv",
            Title = "Chọn nơi lưu báo cáo (sẽ xuất cả CSV và TXT)"
        };

        if (dlg.ShowDialog() != true) return;

        string basePath = Path.Combine(
            Path.GetDirectoryName(dlg.FileName) ?? ".",
            Path.GetFileNameWithoutExtension(dlg.FileName));

        string csvPath = basePath + ".csv";
        string txtPath = basePath + ".txt";

        var csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("TenAnh,Loai,TrangThai,ChiTiet");
        foreach (var row in _reportRows)
        {
            csvBuilder.AppendLine($"{EscapeCsv(row.BaseName)},{EscapeCsv(row.Kind)},{EscapeCsv(row.Status)},{EscapeCsv(row.Detail)}");
        }
        File.WriteAllText(csvPath, csvBuilder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var txtBuilder = new StringBuilder();
        txtBuilder.AppendLine("BÁO CÁO XỬ LÝ PHOTO FILE MANAGER");
        txtBuilder.AppendLine($"Thời gian: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        txtBuilder.AppendLine(_lastSummaryText);
        txtBuilder.AppendLine(new string('-', 70));
        foreach (var row in _reportRows)
        {
            txtBuilder.AppendLine($"[{row.Kind}] {row.BaseName} | {row.Status} | {row.Detail}");
        }
        File.WriteAllText(txtPath, txtBuilder.ToString(), Encoding.UTF8);

        MessageBox.Show($"Đã xuất báo cáo thành công:\n- {csvPath}\n- {txtPath}", "Xuất báo cáo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string EscapeCsv(string value)
    {
        string escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }

    private void SetBusy(bool busy)
    {
        BtnRun.IsEnabled = !busy;
        BtnExportReport.IsEnabled = !busy && _reportRows.Count > 0;
    }

    private sealed record ReportRow(string BaseName, string Kind, string Status, string Detail, bool HasDuplicate);

    private sealed class FolderSettings
    {
        public string? JpegSource { get; set; }
        public string? RawSource { get; set; }
        public string? JpegDest { get; set; }
        public string? RawDest { get; set; }
    }
}
