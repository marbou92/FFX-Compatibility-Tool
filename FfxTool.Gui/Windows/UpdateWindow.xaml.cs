using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FfxTool.Gui
{
    /// <summary>
    /// The in-app updater — one themed window that walks the whole flow:
    /// check → offer (with the release's vivi-style what's-new notes from
    /// changelog.json) → download (an AE-timeline progress: rounded track,
    /// primary fill, a playhead riding the fill's edge and overhanging the
    /// track, and stage chips that light up Check → Download → Verify →
    /// Restart) → restart into the new build. Up-to-date and error states
    /// round it off; the error state always keeps the release page as the
    /// manual escape hatch.
    /// </summary>
    public partial class UpdateWindow : Window
    {
        private ChangelogEntry _entry;   // the version being offered
        private string _stagedPath;      // verified download, waiting for restart
        private bool _downloading;

        private Border[] _stageChips;
        private IconGlyph[] _stageIcons;
        private TextBlock[] _stageLabels;

        /// <summary>Opens with a fresh check (the About page's button).</summary>
        public UpdateWindow() : this(null) { }

        /// <summary>Opens straight onto a known update (the startup toast).</summary>
        public UpdateWindow(ChangelogEntry known)
        {
            InitializeComponent();
            BuildStageChips();
            StartSweep();
            if (known != null) ShowOffer(known);
            else StartCheck();
        }

        // ---------- states ----------

        private void StartCheck()
        {
            ShowPanel(CheckingPanel);
            UpdateService.CheckNowAsync((result, entry) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (result.Status == UpdateCheckStatus.UpdateAvailable)
                    ShowOffer(entry);
                else if (result.Status == UpdateCheckStatus.Error)
                    ShowError(result.Message, allowRetry: true);
                else
                    ShowUpToDate(result.Message);
            })));
        }

        private void ShowOffer(ChangelogEntry entry)
        {
            if (entry == null)
            {
                ShowError("The update's details didn't arrive — check your connection and try again.", allowRetry: true);
                return;
            }
            _entry = entry;
            StopSweep();
            UpdateService.MarkSeen();

            FromChip.Text = "v" + AppInfo.Version;
            ToChip.Text = "v" + entry.Version;
            OfferHeadline.Text = "Version " + entry.Version + " is out.";
            OfferDescription.Text = string.IsNullOrEmpty(entry.Description)
                ? "The release page has the details."
                : entry.Description;
            ChangelogView.BuildInto(OfferSections, entry, includeDescription: false);

            NightlyNote.Visibility = UpdateService.IsNightlyBuild
                ? Visibility.Visible : Visibility.Collapsed;

            // a feed-less fallback entry has no file name — the download
            // button steps aside and the release page becomes the path
            bool downloadable = !string.IsNullOrEmpty(entry.File);
            DownloadButton.Visibility = downloadable ? Visibility.Visible : Visibility.Collapsed;
            NoDownloadNote.Visibility = downloadable ? Visibility.Collapsed : Visibility.Visible;

            ShowPanel(OfferPanel);
        }

        private void ShowUpToDate(string message)
        {
            StopSweep();
            UpdateService.MarkSeen();
            UpToDateMessage.Text = message;
            ShowPanel(UpToDatePanel);
        }

        private void ShowError(string message, bool allowRetry)
        {
            StopSweep();
            _downloading = false;
            ErrorMessage.Text = message;
            ErrorRetryButton.Visibility = allowRetry ? Visibility.Visible : Visibility.Collapsed;
            ShowPanel(ErrorPanel);
        }

        private void ShowPanel(UIElement panel)
        {
            CheckingPanel.Visibility = panel == CheckingPanel ? Visibility.Visible : Visibility.Collapsed;
            OfferPanel.Visibility = panel == OfferPanel ? Visibility.Visible : Visibility.Collapsed;
            ProgressPanel.Visibility = panel == ProgressPanel ? Visibility.Visible : Visibility.Collapsed;
            ReadyPanel.Visibility = panel == ReadyPanel ? Visibility.Visible : Visibility.Collapsed;
            UpToDatePanel.Visibility = panel == UpToDatePanel ? Visibility.Visible : Visibility.Collapsed;
            ErrorPanel.Visibility = panel == ErrorPanel ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- download ----------

        private void Download_Click(object sender, RoutedEventArgs e)
        {
            if (_entry == null || string.IsNullOrEmpty(_entry.File) || _downloading) return;
            _downloading = true;

            SetStage(1); // Check done, Download running
            ProgressHeadline.Text = "Downloading v" + _entry.Version + "…";
            ProgressStageText.Text = "Contacting the release servers…";
            ProgressStats.Text = "";
            ShowPanel(ProgressPanel);

            UpdateService.DownloadAsync(_entry,
                progress => Dispatcher.BeginInvoke(new Action(() => ApplyProgress(progress))),
                stagedPath => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _downloading = false;
                    _stagedPath = stagedPath;
                    ShowReady();
                })),
                error => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _downloading = false;
                    ShowError(error, allowRetry: true);
                })),
                () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _downloading = false;
                    ShowOffer(_entry); // canceled — back to the offer
                })));
        }

        private void ApplyProgress(UpdateProgress p)
        {
            if (p == null) return;
            if (p.Stage == "verify")
            {
                SetStage(2);
                ProgressStageText.Text = string.IsNullOrEmpty(_entry == null ? null : _entry.Sha256)
                    ? "Confirming the download…"
                    : "Verifying the download against the release's SHA-256…";
                ProgressStats.Text = UpdateService.FmtBytes(p.BytesReceived);
                return;
            }

            SetStage(1);
            ProgressStageText.Text = "Downloading…";
            if (p.Fraction >= 0)
            {
                AnimateFill(p.Fraction);
                ProgressStats.Text = UpdateService.FmtBytes(p.BytesReceived) +
                                     " of " + UpdateService.FmtBytes(p.TotalBytes) +
                                     (p.Speed != null ? "  ·  " + p.Speed : "");
            }
            else
            {
                IndeterminateFill();
                ProgressStats.Text = UpdateService.FmtBytes(p.BytesReceived) +
                                     (p.Speed != null ? "  ·  " + p.Speed : "");
            }
        }

        private void ShowReady()
        {
            SetStage(3);
            StopFillAnimations();
            ReadyHeadline.Text = "v" + _entry.Version + " downloaded and verified";
            ShowPanel(ReadyPanel);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            UpdateService.CancelDownload(); // the canceled callback re-offers
        }

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_stagedPath)) return;
            try
            {
                RestartButton.IsEnabled = false;
                // swap + relaunch; if this call returns, something went wrong
                // BEFORE the new process could start — the old exe is back
                UpdateService.ApplyUpdate(_stagedPath);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                RestartButton.IsEnabled = true;
                ShowError(UpdateService.FriendlyError(ex), allowRetry: false);
            }
        }

        private void Retry_Click(object sender, RoutedEventArgs e)
        {
            if (_entry != null) ShowOffer(_entry);
            else StartCheck();
        }

        private void ReleasePage_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(_entry != null && !string.IsNullOrEmpty(_entry.Url)
                ? _entry.Url
                : ChangelogFeed.RepoUrl + "/releases");
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_downloading) UpdateService.CancelDownload();
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_downloading) UpdateService.CancelDownload();
            base.OnClosing(e);
        }

        // ---------- the AE-timeline visuals ----------

        private void BuildStageChips()
        {
            var stages = new[]
            {
                (icon: "Eye", label: "Check"),
                (icon: "Upload", label: "Download"),
                (icon: "Verified", label: "Verify"),
                (icon: "SwapHoriz", label: "Restart")
            };
            _stageChips = new Border[stages.Length];
            _stageIcons = new IconGlyph[stages.Length];
            _stageLabels = new TextBlock[stages.Length];

            for (int i = 0; i < stages.Length; i++)
            {
                var icon = new IconGlyph
                {
                    IconName = stages[i].icon,
                    Width = 13,
                    Height = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (i == 1)
                {
                    // the Download glyph is the set's Upload arrow — flipped
                    // 180° it points down, and no new icon has to exist
                    icon.RenderTransformOrigin = new Point(0.5, 0.5);
                    icon.RenderTransform = new RotateTransform(180);
                }
                var label = new TextBlock
                {
                    Text = stages[i].label,
                    FontSize = 11.5,
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(11, 5, 11, 5),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                chip.SetResourceReference(Border.BackgroundProperty, "B.SC");
                label.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");

                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(icon);
                content.Children.Add(label);
                chip.Child = content;

                StageRow.Children.Add(chip);
                _stageChips[i] = chip;
                _stageIcons[i] = icon;
                _stageLabels[i] = label;
            }
            SetStage(0);
        }

        /// <summary>0 = only Check active; the Ready state passes 3 (the
        /// Restart chip) — chips before it read as done.</summary>
        private void SetStage(int active)
        {
            for (int i = 0; i < _stageChips.Length; i++)
            {
                var chip = _stageChips[i];
                var icon = _stageIcons[i];
                var label = _stageLabels[i];
                chip.Opacity = i > active ? 0.45 : 1.0;

                if (i == active)
                {
                    // the running stage wears the primary container pill
                    chip.SetResourceReference(Border.BackgroundProperty, "B.PrimaryContainer");
                    icon.SetResourceReference(IconGlyph.ForegroundProperty, "B.OnPrimaryContainer");
                    label.SetResourceReference(TextBlock.ForegroundProperty, "B.OnPrimaryContainer");
                    label.FontWeight = FontWeights.SemiBold;
                }
                else
                {
                    chip.SetResourceReference(Border.BackgroundProperty, "B.SC");
                    // finished stages show primary (the check drew the line
                    // under them), pending ones stay quiet
                    icon.SetResourceReference(IconGlyph.ForegroundProperty,
                        i < active ? "B.Primary" : "B.OnSurfaceVariant");
                    label.SetResourceReference(TextBlock.ForegroundProperty, "B.OnSurfaceVariant");
                    label.FontWeight = FontWeights.Normal;
                }
            }
        }

        private void AnimateFill(double fraction)
        {
            double track = TrackGrid.ActualWidth > 0 ? TrackGrid.ActualWidth : 512;
            fraction = Math.Min(1.0, Math.Max(0.0, fraction));
            // a sliver of fill as soon as bytes arrive, dead-zero at 0%
            double width = fraction <= 0 ? 0 : Math.Max(6, track * fraction);
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(180);

            Playhead.Visibility = Visibility.Visible;
            FillBar.BeginAnimation(WidthProperty,
                new DoubleAnimation(width, dur) { EasingFunction = ease });
            PlayheadMove.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(width - 1.25, dur) { EasingFunction = ease });
        }

        private void IndeterminateFill()
        {
            // total size unknown: the fill breathes between 10% and 45%
            double track = TrackGrid.ActualWidth > 0 ? TrackGrid.ActualWidth : 512;
            Playhead.Visibility = Visibility.Collapsed;
            var breath = new DoubleAnimation(track * 0.10, track * 0.45,
                TimeSpan.FromMilliseconds(850))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            FillBar.BeginAnimation(WidthProperty, breath);
            PlayheadMove.BeginAnimation(TranslateTransform.XProperty, null);
        }

        private void StopFillAnimations()
        {
            FillBar.BeginAnimation(WidthProperty, null);
            PlayheadMove.BeginAnimation(TranslateTransform.XProperty, null);
            FillBar.Width = TrackGrid.ActualWidth > 0 ? TrackGrid.ActualWidth : 512;
            PlayheadMove.X = TrackGrid.ActualWidth - 1.25;
        }

        private void StartSweep()
        {
            var glide = new DoubleAnimation(-96, 520, TimeSpan.FromMilliseconds(1150))
            {
                RepeatBehavior = RepeatBehavior.Forever
            };
            CheckSweepMove.BeginAnimation(TranslateTransform.XProperty, glide);
        }

        private void StopSweep()
        {
            CheckSweepMove.BeginAnimation(TranslateTransform.XProperty, null);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { /* browser launch refused — nothing sensible to do */ }
        }
    }
}
