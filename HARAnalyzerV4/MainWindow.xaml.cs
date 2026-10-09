using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using IOPath = System.IO.Path;

namespace HARAnalyzerV4
{
    public partial class MainWindow : Window
    {
        private readonly List<HarItem> allItems = new();
        private string? currentHarFile;
        private JsonElement? loadedHarRoot;

        public MainWindow()
        {
            InitializeComponent();

            // MainWindow.xaml loads the embedded window icon.
            StatisticsText.Text =
                "Select a frame to display performance statistics.";

            TimelineTitle.Text = "Session Timeline";
            TimelineSummary.Text = "";

            Loaded += (_, _) => DrawTimeline();
        }

        // ------------------------------------------------------------
        // Open and load HAR
        // ------------------------------------------------------------

        private void OpenHar_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                Filter = "HAR files (*.har)|*.har|All files (*.*)|*.*",
                Title = "Open HAR file"
            };

            if (dialog.ShowDialog(this) == true)
            {
                LoadHarFile(dialog.FileName);
            }
        }

        private void LoadHarFile(string fileName)
        {
            try
            {
                StatusText.Text = "Loading HAR...";

                string json = File.ReadAllText(fileName);
                using JsonDocument document = JsonDocument.Parse(json);

                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty(
                        "log", out JsonElement log) ||
                    log.ValueKind != JsonValueKind.Object ||
                    !log.TryGetProperty(
                        "entries", out JsonElement entries) ||
                    entries.ValueKind != JsonValueKind.Array)
                {
                    MessageBox.Show(
                        this,
                        "Invalid HAR file. The log.entries array was not found.",
                        "HAR Analyzer V4",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    StatusText.Text = "Invalid HAR file.";
                    return;
                }

                List<HarItem> loadedItems = new();
                int frame = 0;

                foreach (JsonElement entry in entries.EnumerateArray())
                {
                    frame++;

                    if (entry.ValueKind != JsonValueKind.Object ||
                        !entry.TryGetProperty(
                            "request", out JsonElement request) ||
                        request.ValueKind != JsonValueKind.Object ||
                        !entry.TryGetProperty(
                            "response", out JsonElement response) ||
                        response.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    string method = GetStringProperty(request, "method");
                    string url = GetStringProperty(request, "url");
                    int status = GetIntProperty(response, "status");
                    double time = GetDoubleProperty(entry, "time");

                    string protocol =
                        GetStringProperty(response, "httpVersion");

                    if (string.IsNullOrWhiteSpace(protocol))
                    {
                        protocol =
                            GetStringProperty(request, "httpVersion");
                    }

                    string host = "";

                    if (Uri.TryCreate(
                            url, UriKind.Absolute, out Uri? uri))
                    {
                        host = uri.Host;
                    }

                    string contentType = "";
                    long bodySize = 0;

                    if (response.TryGetProperty(
                            "content", out JsonElement content) &&
                        content.ValueKind == JsonValueKind.Object)
                    {
                        contentType =
                            GetStringProperty(content, "mimeType");

                        bodySize =
                            GetLongProperty(content, "size");
                    }

                    if (bodySize <= 0)
                    {
                        bodySize =
                            GetLongProperty(response, "bodySize");
                    }

                    loadedItems.Add(new HarItem
                    {
                        FrameNumber = frame,
                        Method = method,
                        Status = status,
                        Protocol = protocol,
                        Host = host,
                        Url = url,
                        ContentType = contentType,
                        BodySizeValue = bodySize,
                        TimeValue = time,
                        StartedDateTime = ParseStartedDateTime(entry),
                        Entry = entry.Clone()
                    });
                }

                HarGrid.ItemsSource = null;

                allItems.Clear();
                allItems.AddRange(loadedItems);

                loadedHarRoot = document.RootElement.Clone();
                currentHarFile = IOPath.GetFullPath(fileName);

                HarGrid.ItemsSource = allItems;

                SearchBox.Text = "";
                RequestText.Text = "";
                ResponseText.Text = "";
                BodyText.Text = "";

                StatisticsText.Text =
                    "Select a frame to display performance statistics.";

                FrameCountText.Text = $"Frames: {allItems.Count:N0}";
                VisibleFrameCountText.Text =
                    $"Visible: {allItems.Count:N0}";

                SelectedFrameText.Text = "Selected: none";
                SaveSelectedHarButton.IsEnabled = false;

                Title =
                    "HAR Analyzer V4 - " + IOPath.GetFileName(fileName);

                StatusText.Text =
                    $"Loaded {allItems.Count:N0} frames.";

                DrawTimeline();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error loading HAR.";

                MessageBox.Show(
                    this,
                    ex.Message,
                    "HAR Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ------------------------------------------------------------
        // Export selected frames
        // ------------------------------------------------------------

        private void SaveSelectedHar_Click(
            object sender, RoutedEventArgs e)
        {
            List<HarItem> selected = HarGrid.SelectedItems
                .Cast<HarItem>()
                .OrderBy(item => item.FrameNumber)
                .ToList();

            if (selected.Count == 0 || !loadedHarRoot.HasValue)
            {
                MessageBox.Show(
                    this,
                    "Select one or more frames first " +
                    "(Ctrl+click or Shift+click).",
                    "Save selected HAR",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            SaveFileDialog dialog = new()
            {
                Filter = "HAR files (*.har)|*.har",
                Title = $"Save {selected.Count:N0} selected frames",
                DefaultExt = ".har",
                AddExtension = true,
                OverwritePrompt = true,
                FileName =
                    IOPath.GetFileNameWithoutExtension(
                        currentHarFile ?? "capture") + "-selected.har"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            string? temporaryFile = null;

            try
            {
                if (currentHarFile != null &&
                    string.Equals(
                        IOPath.GetFullPath(dialog.FileName),
                        IOPath.GetFullPath(currentHarFile),
                        StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        this,
                        "Choose a different filename to keep " +
                        "the original HAR file.",
                        "Save selected HAR",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                temporaryFile = IOPath.Combine(
                    IOPath.GetDirectoryName(dialog.FileName)!,
                    "." + Guid.NewGuid().ToString("N") + ".tmp");

                using (FileStream stream = new(
                    temporaryFile,
                    FileMode.CreateNew,
                    FileAccess.Write))
                {
                    HarSelectionExporter.Write(
                        stream,
                        loadedHarRoot.Value,
                        selected.Select(item => item.Entry));
                }

                File.Move(
                    temporaryFile,
                    dialog.FileName,
                    overwrite: true);

                temporaryFile = null;

                StatusText.Text =
                    $"Saved {selected.Count:N0} frames to " +
                    IOPath.GetFileName(dialog.FileName) + ".";

                MessageBox.Show(
                    this,
                    $"Saved {selected.Count:N0} selected frames.\n\n" +
                    dialog.FileName,
                    "Save selected HAR",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error saving selected HAR.";

                MessageBox.Show(
                    this,
                    ex.Message,
                    "Save selected HAR",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (temporaryFile != null)
                {
                    try
                    {
                        File.Delete(temporaryFile);
                    }
                    catch
                    {
                        // Preserve the original save error.
                    }
                }
            }
        }

        // ------------------------------------------------------------
        // Search
        // ------------------------------------------------------------

        private void SearchBox_TextChanged(
            object sender, TextChangedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void ClearSearch_Click(
            object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        private void ApplySearchFilter()
        {
            if (HarGrid == null || HarGrid.ItemsSource == null)
            {
                return;
            }

            ICollectionView view =
                CollectionViewSource.GetDefaultView(
                    HarGrid.ItemsSource);

            string search = SearchBox?.Text?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(search))
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = obj =>
                {
                    if (obj is not HarItem item)
                    {
                        return false;
                    }

                    return
                        Contains(
                            item.FrameNumber.ToString(
                                CultureInfo.InvariantCulture), search) ||
                        Contains(
                            item.Status.ToString(
                                CultureInfo.InvariantCulture), search) ||
                        Contains(item.Method, search) ||
                        Contains(item.Protocol, search) ||
                        Contains(item.Host, search) ||
                        Contains(item.Url, search) ||
                        Contains(item.ContentType, search) ||
                        Contains(item.BodySize, search) ||
                        Contains(item.Time, search);
                };
            }

            view.Refresh();

            int visible = 0;

            foreach (object unused in view)
            {
                visible++;
            }

            VisibleFrameCountText.Text = $"Visible: {visible:N0}";

            StatusText.Text = string.IsNullOrWhiteSpace(search)
                ? $"Showing all {visible:N0} frames."
                : $"Search: {visible:N0} of {allItems.Count:N0} frames.";

            DrawTimeline();
        }

        private static bool Contains(string? value, string search)
        {
            return !string.IsNullOrEmpty(value) &&
                value.IndexOf(
                    search,
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------
        // Selection
        // ------------------------------------------------------------

        private void HarGrid_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            if (SaveSelectedHarButton == null)
            {
                return;
            }

            int selectedCount = HarGrid.SelectedItems.Count;

            SaveSelectedHarButton.IsEnabled =
                selectedCount > 0 && loadedHarRoot.HasValue;

            if (HarGrid.SelectedItem is not HarItem item)
            {
                SelectedFrameText.Text = "Selected: none";
                RequestText.Text = "";
                ResponseText.Text = "";
                BodyText.Text = "";

                StatisticsText.Text =
                    "Select a frame to display performance statistics.";

                DrawTimeline();
                return;
            }

            SelectedFrameText.Text =
                $"Selected: {selectedCount:N0} frame(s) " +
                $"— active #{item.FrameNumber}";

            ShowRequest(item.Entry);
            ShowResponse(item.Entry);
            ShowResponseBody(item.Entry);
            ShowStatistics(item);

            DrawTimeline();
        }

        // ------------------------------------------------------------
        // Request details
        // ------------------------------------------------------------

        private void ShowRequest(JsonElement entry)
        {
            if (!entry.TryGetProperty(
                    "request", out JsonElement request))
            {
                RequestText.Text = "";
                return;
            }

            StringBuilder output = new();

            string method = GetStringProperty(request, "method");
            string url = GetStringProperty(request, "url");
            string version = GetStringProperty(request, "httpVersion");

            output.AppendLine($"{method} {url} {version}");
            output.AppendLine();

            AppendHeaders(output, request);

            if (request.TryGetProperty(
                    "queryString", out JsonElement query) &&
                query.ValueKind == JsonValueKind.Array &&
                query.GetArrayLength() > 0)
            {
                output.AppendLine();
                output.AppendLine(
                    "---------------- QUERY STRING ----------------");
                output.AppendLine();

                foreach (JsonElement parameter in query.EnumerateArray())
                {
                    output.AppendLine(
                        GetStringProperty(parameter, "name") +
                        " = " +
                        GetStringProperty(parameter, "value"));
                }
            }

            if (request.TryGetProperty(
                    "postData", out JsonElement postData) &&
                postData.ValueKind == JsonValueKind.Object)
            {
                output.AppendLine();
                output.AppendLine(
                    "---------------- REQUEST BODY ----------------");
                output.AppendLine();

                string mimeType =
                    GetStringProperty(postData, "mimeType");

                if (!string.IsNullOrWhiteSpace(mimeType))
                {
                    output.AppendLine($"Content-Type: {mimeType}");
                    output.AppendLine();
                }

                string text = GetStringProperty(postData, "text");
                string encoding =
                    GetStringProperty(postData, "encoding");

                if (encoding.Equals(
                        "base64",
                        StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        byte[] bytes =
                            Convert.FromBase64String(text);

                        if (LooksLikeText(bytes))
                        {
                            text = Encoding.UTF8.GetString(bytes);
                        }
                        else
                        {
                            output.AppendLine(
                                "[Binary request body: " +
                                FormatBytes(bytes.Length) + "]");

                            RequestText.Text = output.ToString();
                            return;
                        }
                    }
                    catch
                    {
                        output.AppendLine(
                            "[Unable to decode Base64 request body]");

                        RequestText.Text = output.ToString();
                        return;
                    }
                }

                output.AppendLine(PrettyPrintJson(text));
            }

            RequestText.Text = output.ToString();
        }

        // ------------------------------------------------------------
        // Response details and body
        // ------------------------------------------------------------

        private void ShowResponse(JsonElement entry)
        {
            if (!entry.TryGetProperty(
                    "response", out JsonElement response))
            {
                ResponseText.Text = "";
                return;
            }

            StringBuilder output = new();

            string version =
                GetStringProperty(response, "httpVersion");

            int status = GetIntProperty(response, "status");
            string statusText =
                GetStringProperty(response, "statusText");

            output.AppendLine($"{version} {status} {statusText}");
            output.AppendLine();

            AppendHeaders(output, response);

            if (response.TryGetProperty(
                    "content", out JsonElement content) &&
                content.ValueKind == JsonValueKind.Object)
            {
                output.AppendLine();
                output.AppendLine(
                    "---------------- CONTENT ----------------");
                output.AppendLine();

                output.AppendLine(
                    "MIME Type: " +
                    GetStringProperty(content, "mimeType"));

                output.AppendLine(
                    "Size: " +
                    FormatBytes(GetLongProperty(content, "size")));
            }

            ResponseText.Text = output.ToString();
        }

        private void ShowResponseBody(JsonElement entry)
        {
            if (!entry.TryGetProperty(
                    "response", out JsonElement response))
            {
                BodyText.Text = "(No response)";
                return;
            }

            if (!response.TryGetProperty(
                    "content", out JsonElement content) ||
                content.ValueKind != JsonValueKind.Object)
            {
                BodyText.Text = "(No response content)";
                return;
            }

            if (!content.TryGetProperty(
                    "text", out JsonElement textElement) ||
                textElement.ValueKind != JsonValueKind.String)
            {
                BodyText.Text =
                    "(No response body recorded in the HAR)";
                return;
            }

            string text = textElement.GetString() ?? "";

            if (string.IsNullOrEmpty(text))
            {
                BodyText.Text = "(Empty response body)";
                return;
            }

            string encoding =
                GetStringProperty(content, "encoding");

            if (encoding.Equals(
                    "base64", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(text);

                    if (LooksLikeText(bytes))
                    {
                        text = Encoding.UTF8.GetString(bytes);
                    }
                    else
                    {
                        BodyText.Text =
                            "Binary response body\n\n" +
                            "MIME Type: " +
                            GetStringProperty(content, "mimeType") +
                            "\nSize: " + FormatBytes(bytes.Length) +
                            "\nEncoding: Base64";

                        return;
                    }
                }
                catch
                {
                    BodyText.Text =
                        "(Unable to decode Base64 response body)";
                    return;
                }
            }

            BodyText.Text = PrettyPrintJson(text);
        }

        // ------------------------------------------------------------
        // Statistics
        // ------------------------------------------------------------

        private void ShowStatistics(HarItem item)
        {
            JsonElement entry = item.Entry;
            StringBuilder sb = new();

            sb.AppendLine($"FRAME #{item.FrameNumber}");
            sb.AppendLine($"{item.Method} {item.Url}");
            sb.AppendLine();
            sb.AppendLine("ACTUAL PERFORMANCE");
            sb.AppendLine("------------------");

            DateTimeOffset? start = ParseStartedDateTime(entry);

            double blocked = GetTiming(entry, "blocked");
            double dns = GetTiming(entry, "dns");
            double connect = GetTiming(entry, "connect");
            double ssl = GetTiming(entry, "ssl");
            double send = GetTiming(entry, "send");
            double wait = GetTiming(entry, "wait");
            double receive = GetTiming(entry, "receive");
            double total = item.TimeValue;

            double connectionOffset =
                Positive(blocked) + Positive(dns) + Positive(connect);

            double requestDoneOffset =
                connectionOffset + Positive(send);

            double responseStartOffset =
                requestDoneOffset + Positive(wait);

            double responseDoneOffset =
                responseStartOffset + Positive(receive);

            AppendTimestamp(
                sb, "ClientConnected:", start, true);

            AppendTimestamp(
                sb, "ClientBeginRequest:", start, true);

            AppendTimestamp(
                sb, "GotRequestHeaders:", start, true);

            AppendTimestamp(
                sb, "ClientDoneRequest:",
                AddMs(start, requestDoneOffset), true);

            AppendDuration(sb, "Determine Gateway:", -1);
            AppendDuration(sb, "DNS Lookup:", dns);
            AppendDuration(sb, "TCP/IP Connect:", connect);
            AppendDuration(sb, "HTTPS Handshake:", ssl);

            AppendTimestamp(
                sb, "ServerConnected:",
                AddMs(start, connectionOffset), true);

            sb.AppendLine($"{"FiddlerBeginRequest:",-23}N/A");

            AppendTimestamp(
                sb, "ServerGotRequest:",
                AddMs(start, requestDoneOffset), true);

            AppendTimestamp(
                sb, "ServerBeginResponse:",
                AddMs(start, responseStartOffset), true);

            AppendTimestamp(
                sb, "GotResponseHeaders:",
                AddMs(start, responseStartOffset), true);

            AppendTimestamp(
                sb, "ServerDoneResponse:",
                AddMs(start, responseDoneOffset), true);

            AppendTimestamp(
                sb, "ClientBeginResponse:",
                AddMs(start, responseStartOffset), true);

            AppendTimestamp(
                sb, "ClientDoneResponse:",
                AddMs(start, total >= 0 ? total : responseDoneOffset),
                true);

            sb.AppendLine();
            sb.AppendLine(
                $"Overall Elapsed:       {FormatElapsed(total)}");

            sb.AppendLine();
            sb.AppendLine("HAR TIMINGS");
            sb.AppendLine("-----------");

            AppendDuration(sb, "Blocked:", blocked);
            AppendDuration(sb, "DNS:", dns);
            AppendDuration(sb, "Connect:", connect);
            AppendDuration(sb, "SSL/TLS:", ssl);
            AppendDuration(sb, "Send:", send);
            AppendDuration(sb, "Wait / TTFB:", wait);
            AppendDuration(sb, "Receive:", receive);

            sb.AppendLine();
            sb.AppendLine("RESPONSE");
            sb.AppendLine("--------");

            sb.AppendLine($"Status:                {item.Status}");
            sb.AppendLine($"Protocol:              {item.Protocol}");
            sb.AppendLine($"Content-Type:          {item.ContentType}");

            sb.AppendLine(
                "Body Size:             " +
                FormatBytes(item.BodySizeValue));

            sb.AppendLine();
            sb.AppendLine("* HAR-derived approximation.");
            sb.AppendLine(
                "Standard HAR files do not contain " +
                "every Fiddler proxy timestamp.");

            StatisticsText.Text = sb.ToString();
        }

        private static void AppendTimestamp(
            StringBuilder sb,
            string name,
            DateTimeOffset? value,
            bool approximate = false)
        {
            string text = value.HasValue
                ? value.Value.ToString("HH:mm:ss.fff")
                : "N/A";

            sb.AppendLine(
                $"{name,-23}{text}{(approximate ? " *" : "")}");
        }

        private static void AppendDuration(
            StringBuilder sb, string name, double value)
        {
            string text = value < 0
                ? "N/A"
                : $"{value:0.###}ms";

            sb.AppendLine($"{name,-23}{text}");
        }

        // ------------------------------------------------------------
        // Timeline
        // ------------------------------------------------------------

        private void TimelineCanvas_SizeChanged(
            object sender, SizeChangedEventArgs e)
        {
            DrawTimeline();
        }

        private void DrawTimeline()
        {
            if (TimelineCanvas == null ||
                TimelineTitle == null ||
                TimelineSummary == null)
            {
                return;
            }

            TimelineCanvas.Children.Clear();

            IEnumerable<HarItem> source;

            if (HarGrid != null && HarGrid.SelectedItems.Count > 1)
            {
                source = HarGrid.SelectedItems
                    .Cast<HarItem>()
                    .OrderBy(x => x.StartedDateTime);
            }
            else if (HarGrid != null &&
                     HarGrid.SelectedItem is HarItem selected)
            {
                source = new[] { selected };
            }
            else if (HarGrid != null && HarGrid.ItemsSource != null)
            {
                ICollectionView view =
                    CollectionViewSource.GetDefaultView(
                        HarGrid.ItemsSource);

                source = view.Cast<object>()
                    .OfType<HarItem>()
                    .Take(100)
                    .ToList();
            }
            else
            {
                source = allItems.Take(100);
            }

            List<HarItem> items = source
                .Where(x => x.StartedDateTime.HasValue)
                .ToList();

            if (items.Count == 0)
            {
                TimelineTitle.Text = "Session Timeline";
                TimelineSummary.Text = "No timing data available";

                AddTimelineText(
                    "No startedDateTime information is available.",
                    20, 30, 13, Brushes.DimGray);

                return;
            }

            DateTimeOffset first =
                items.Min(x => x.StartedDateTime!.Value);

            DateTimeOffset last = items.Max(
                x => x.StartedDateTime!.Value.AddMilliseconds(
                    Math.Max(0, x.TimeValue)));

            double spanMs = Math.Max(
                1, (last - first).TotalMilliseconds);

            TimelineTitle.Text = items.Count == 1
                ? $"Single Session Timeline - Frame #{items[0].FrameNumber}"
                : $"Timeline - {items.Count} sessions";

            TimelineSummary.Text =
                $"Span: {FormatElapsed(spanMs)}";

            const double leftMargin = 250;
            const double topMargin = 50;
            const double rowHeight = 30;
            const double barHeight = 16;

            double width = Math.Max(
                TimelineCanvas.ActualWidth, 1100);

            double graphWidth = Math.Max(
                500, width - leftMargin - 50);

            TimelineCanvas.Height = Math.Max(
                250,
                topMargin + items.Count * rowHeight + 40);

            const int ticks = 10;

            for (int i = 0; i <= ticks; i++)
            {
                double x =
                    leftMargin + graphWidth * i / ticks;

                Line gridLine = new()
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 28,
                    Y2 = TimelineCanvas.Height - 10,
                    Stroke = Brushes.Gainsboro,
                    StrokeThickness = 1
                };

                TimelineCanvas.Children.Add(gridLine);

                double tickMs = spanMs * i / ticks;

                AddTimelineText(
                    FormatTimelineTick(tickMs),
                    x + 3, 6, 11, Brushes.DimGray);
            }

            int row = 0;

            foreach (HarItem item in items)
            {
                double y = topMargin + row * rowHeight;

                AddTimelineText(
                    $"#{item.FrameNumber}  {item.Method}  {item.Host}",
                    8, y - 2, 12, Brushes.Black);

                double offsetMs =
                    (item.StartedDateTime!.Value - first)
                    .TotalMilliseconds;

                double x =
                    leftMargin + offsetMs / spanMs * graphWidth;

                double barWidth = Math.Max(
                    2,
                    Math.Max(1, item.TimeValue) /
                    spanMs * graphWidth);

                Rectangle bar = new()
                {
                    Width = barWidth,
                    Height = barHeight,
                    Fill = GetTimelineBrush(item.ContentType),
                    Stroke = Brushes.DimGray,
                    StrokeThickness = 0.5,
                    ToolTip =
                        $"Frame #{item.FrameNumber}\n" +
                        $"{item.Method} {item.Url}\n" +
                        $"Status: {item.Status}\n" +
                        $"Start: {item.StartedDateTime:HH:mm:ss.fff}\n" +
                        $"Duration: {item.TimeValue:0.###} ms"
                };

                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
                TimelineCanvas.Children.Add(bar);

                double firstByteOffset =
                    Positive(GetTiming(item.Entry, "blocked")) +
                    Positive(GetTiming(item.Entry, "dns")) +
                    Positive(GetTiming(item.Entry, "connect")) +
                    Positive(GetTiming(item.Entry, "send")) +
                    Positive(GetTiming(item.Entry, "wait"));

                if (firstByteOffset >= 0 &&
                    firstByteOffset <= item.TimeValue)
                {
                    double markerX =
                        x + firstByteOffset / spanMs * graphWidth;

                    Line marker = new()
                    {
                        X1 = markerX,
                        X2 = markerX,
                        Y1 = y - 3,
                        Y2 = y + barHeight + 3,
                        Stroke = Brushes.Red,
                        StrokeThickness = 1.5,
                        ToolTip =
                            $"First response byte: {firstByteOffset:0.###} ms"
                    };

                    TimelineCanvas.Children.Add(marker);
                }

                row++;
            }
        }

        private void AddTimelineText(
            string text,
            double x,
            double y,
            double size,
            Brush brush)
        {
            TextBlock block = new()
            {
                Text = text,
                FontSize = size,
                Foreground = brush,
                FontFamily = new FontFamily("Segoe UI")
            };

            Canvas.SetLeft(block, x);
            Canvas.SetTop(block, y);
            TimelineCanvas.Children.Add(block);
        }

        private static Brush GetTimelineBrush(string contentType)
        {
            string type = (contentType ?? "").ToLowerInvariant();

            if (type.Contains("javascript"))
                return Brushes.Goldenrod;

            if (type.Contains("css"))
                return Brushes.SteelBlue;

            if (type.StartsWith("image/"))
                return Brushes.MediumSeaGreen;

            if (type.Contains("json"))
                return Brushes.MediumPurple;

            if (type.Contains("html"))
                return Brushes.CornflowerBlue;

            return Brushes.LightSkyBlue;
        }

        private static string FormatTimelineTick(double milliseconds)
        {
            return milliseconds >= 1000
                ? $"{milliseconds / 1000.0:0.##}s"
                : $"{milliseconds:0}ms";
        }

        // ------------------------------------------------------------
        // SAZ conversion
        // ------------------------------------------------------------

        private async void ConvertSaz_Click(
            object sender, RoutedEventArgs e)
        {
            OpenFileDialog openDialog = new()
            {
                Filter =
                    "Fiddler SAZ files (*.saz)|*.saz|" +
                    "All files (*.*)|*.*",
                Title = "Select Fiddler SAZ file"
            };

            if (openDialog.ShowDialog(this) != true)
            {
                return;
            }

            SaveFileDialog saveDialog = new()
            {
                Filter = "HAR files (*.har)|*.har",
                Title = "Save converted HAR file",
                FileName =
                    IOPath.GetFileNameWithoutExtension(
                        openDialog.FileName) + ".har",
                DefaultExt = ".har",
                AddExtension = true,
                InitialDirectory =
                    IOPath.GetDirectoryName(openDialog.FileName)
            };

            if (saveDialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                StatusText.Text = "Converting SAZ to HAR...";
                Mouse.OverrideCursor = Cursors.Wait;
                IsEnabled = false;

                int sessionCount = await Task.Run(
                    () => SazToHarConverter.Convert(
                        openDialog.FileName,
                        saveDialog.FileName));

                StatusText.Text =
                    $"Converted {sessionCount:N0} sessions.";

                MessageBoxResult result = MessageBox.Show(
                    this,
                    "SAZ conversion completed successfully!\n\n" +
                    $"Sessions converted: {sessionCount:N0}\n\n" +
                    "HAR file:\n" + saveDialog.FileName +
                    "\n\nDo you want to load the generated HAR now?",
                    "SAZ → HAR",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    LoadHarFile(saveDialog.FileName);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "SAZ conversion failed.";

                MessageBox.Show(
                    this,
                    ex.Message,
                    "SAZ → HAR Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
                Mouse.OverrideCursor = null;
            }
        }

        // ------------------------------------------------------------
        // ZIP
        // ------------------------------------------------------------

        private void ZipHar_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(currentHarFile) ||
                !File.Exists(currentHarFile))
            {
                MessageBox.Show(
                    this,
                    "Please open a HAR file first.",
                    "HAR Analyzer V4",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            try
            {
                string directory =
                    IOPath.GetDirectoryName(currentHarFile) ??
                    Environment.CurrentDirectory;

                string zipFile = IOPath.Combine(
                    directory,
                    IOPath.GetFileNameWithoutExtension(currentHarFile) +
                    ".zip");

                if (File.Exists(zipFile))
                {
                    MessageBoxResult result = MessageBox.Show(
                        this,
                        "The ZIP file already exists:\n\n" +
                        zipFile +
                        "\n\nDo you want to replace it?",
                        "HAR Analyzer V4",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result != MessageBoxResult.Yes)
                    {
                        return;
                    }

                    File.Delete(zipFile);
                }

                using (ZipArchive archive = ZipFile.Open(
                    zipFile, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(
                        currentHarFile,
                        IOPath.GetFileName(currentHarFile),
                        CompressionLevel.Optimal);
                }

                FileInfo original = new(currentHarFile);
                FileInfo compressed = new(zipFile);

                double reduction = original.Length > 0
                    ? 100.0 -
                      (double)compressed.Length /
                      original.Length * 100.0
                    : 0;

                StatusText.Text = "HAR compressed successfully.";

                MessageBox.Show(
                    this,
                    "HAR file compressed successfully!\n\n" +
                    $"Original size: {FormatBytes(original.Length)}\n" +
                    $"ZIP size: {FormatBytes(compressed.Length)}\n" +
                    $"Reduction: {reduction:0.0}%\n\n" +
                    "Saved to:\n" + zipFile,
                    "HAR Analyzer V4",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "ZIP operation failed.";

                MessageBox.Show(
                    this,
                    ex.Message,
                    "ZIP Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ------------------------------------------------------------
        // About popup with HARIco.ico
        // ------------------------------------------------------------

        private void About_Click(object sender, RoutedEventArgs e)
        {
            BitmapImage iconSource = new(
                new Uri(
                    "pack://application:,,,/HARIco.ico",
                    UriKind.Absolute));

            Window aboutWindow = new()
            {
                Title = "About HAR Analyzer V4",
                Owner = this,
                Icon = iconSource,
                Width = 430,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation =
                    WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = Brushes.White
            };

            Grid layout = new()
            {
                Margin = new Thickness(24)
            };

            layout.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            layout.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            layout.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(80)
                });

            layout.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

            Image logo = new()
            {
                Source = iconSource,
                Width = 64,
                Height = 64,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            Grid.SetRow(logo, 0);
            Grid.SetColumn(logo, 0);
            layout.Children.Add(logo);

            StackPanel information = new();

            information.Children.Add(new TextBlock
            {
                Text = "HAR Analyzer V4",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 14)
            });

            information.Children.Add(new TextBlock
            {
                Text =
                    "HAR Analysis\n" +
                    "Selected frames to HAR\n" +
                    "SAZ to HAR Converter\n" +
                    "HAR to ZIP\n" +
                    "Statistics and Timeline",
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 18)
            });

            information.Children.Add(new TextBlock
            {
                Text =
                    "By Andrei-Emilian Rachita\n" +
                    "© 2026 PhoeNIXBird Networks\n" +
                    "www.pbnet.ro",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            Grid.SetRow(information, 0);
            Grid.SetColumn(information, 1);
            layout.Children.Add(information);

            Button okButton = new()
            {
                Content = "OK",
                Width = 90,
                Height = 32,
                Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true,
                IsCancel = true
            };

            okButton.Click += (_, _) => aboutWindow.Close();

            Grid.SetRow(okButton, 1);
            Grid.SetColumnSpan(okButton, 2);
            layout.Children.Add(okButton);

            aboutWindow.Content = layout;
            aboutWindow.ShowDialog();
        }

        // ------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------

        private static DateTimeOffset? ParseStartedDateTime(
            JsonElement entry)
        {
            string value =
                GetStringProperty(entry, "startedDateTime");

            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset result))
            {
                return result;
            }

            return null;
        }

        private static double GetTiming(
            JsonElement entry, string name)
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty(
                    "timings", out JsonElement timings) ||
                timings.ValueKind != JsonValueKind.Object ||
                !timings.TryGetProperty(
                    name, out JsonElement value))
            {
                return -1;
            }

            return value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out double result)
                ? result
                : -1;
        }

        private static double Positive(double value)
        {
            return value < 0 ? 0 : value;
        }

        private static DateTimeOffset? AddMs(
            DateTimeOffset? value, double milliseconds)
        {
            return value.HasValue
                ? value.Value.AddMilliseconds(milliseconds)
                : null;
        }

        private static string FormatElapsed(double milliseconds)
        {
            if (milliseconds < 0)
            {
                return "N/A";
            }

            TimeSpan ts = TimeSpan.FromMilliseconds(milliseconds);

            return
                $"{(int)ts.TotalHours}:" +
                $"{ts.Minutes:00}:" +
                $"{ts.Seconds:00}." +
                $"{ts.Milliseconds:000}";
        }

        private static void AppendHeaders(
            StringBuilder output, JsonElement parent)
        {
            if (!parent.TryGetProperty(
                    "headers", out JsonElement headers) ||
                headers.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement header in headers.EnumerateArray())
            {
                output.AppendLine(
                    GetStringProperty(header, "name") + ": " +
                    GetStringProperty(header, "value"));
            }
        }

        private static string GetStringProperty(
            JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(
                    propertyName, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        private static int GetIntProperty(
            JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(
                    propertyName, out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out int result)
                ? result
                : 0;
        }

        private static long GetLongProperty(
            JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(
                    propertyName, out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt64(out long result)
                ? result
                : 0;
        }

        private static double GetDoubleProperty(
            JsonElement element, string propertyName)
        {
            return element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(
                    propertyName, out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out double result)
                ? result
                : 0;
        }

        private static string PrettyPrintJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            string trimmed = text.TrimStart();

            if (!trimmed.StartsWith("{") &&
                !trimmed.StartsWith("["))
            {
                return text;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(text);

                return JsonSerializer.Serialize(
                    document.RootElement,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
            }
            catch
            {
                return text;
            }
        }

        private static bool LooksLikeText(byte[] data)
        {
            if (data.Length == 0)
            {
                return true;
            }

            int sampleLength = Math.Min(data.Length, 4096);
            int controlCharacters = 0;

            for (int i = 0; i < sampleLength; i++)
            {
                byte value = data[i];

                if (value == 0)
                {
                    return false;
                }

                if (value < 9 || (value > 13 && value < 32))
                {
                    controlCharacters++;
                }
            }

            return controlCharacters < sampleLength * 0.05;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0)
                return "N/A";

            if (bytes < 1024)
                return $"{bytes:N0} bytes";

            if (bytes < 1024L * 1024L)
                return $"{bytes / 1024.0:N1} KB";

            if (bytes < 1024L * 1024L * 1024L)
                return $"{bytes / 1024.0 / 1024.0:N2} MB";

            return $"{bytes / 1024.0 / 1024.0 / 1024.0:N2} GB";
        }
    }

    public class HarItem
    {
        public int FrameNumber { get; set; }
        public string Method { get; set; } = "";
        public int Status { get; set; }
        public string Protocol { get; set; } = "";
        public string Host { get; set; } = "";
        public string Url { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long BodySizeValue { get; set; }
        public double TimeValue { get; set; }
        public DateTimeOffset? StartedDateTime { get; set; }
        public JsonElement Entry { get; set; }

        public string BodySize
        {
            get
            {
                if (BodySizeValue < 0)
                    return "N/A";

                if (BodySizeValue < 1024)
                    return $"{BodySizeValue:N0} B";

                if (BodySizeValue < 1024 * 1024)
                    return $"{BodySizeValue / 1024.0:N1} KB";

                return $"{BodySizeValue / 1024.0 / 1024.0:N2} MB";
            }
        }

        public string Time => TimeValue >= 1000
            ? $"{TimeValue / 1000.0:0.00} s"
            : $"{TimeValue:0} ms";
    }
}