using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GlassSidebar
{
    // The conversation: welcome screen, user bubbles, streamed assistant replies with Markdown,
    // typing indicator, copy / retry.
    internal sealed partial class SidebarWindow
    {
        const string SendGlyphOld ="", StopGlyphOld ="", CopyGlyphOld ="", CheckGlyphOld ="", RetryGlyphOld ="";

        // Segoe Fluent Icons code points
        static string G(int code) { return ((char)code).ToString(); }
        static readonly string SendGlyph = G(0xE74A), StopGlyph = G(0xE71A), CopyGlyph = G(0xE8C8),
                               CheckGlyph = G(0xE73E), RetryGlyph = G(0xE72C);
        static readonly int[] IdeaIcons = { 0xE8A5, 0xE897, 0xE70F, 0xE943 };

        // current conversation; saved to disk after every message
        ChatSession session = new ChatSession();
        List<ChatSession> allChats = new List<ChatSession>();
        List<ChatMessage> history { get { return session.Messages; } }
        CancellationTokenSource streamCts;
        FrameworkElement lastReply;      // so Retry can replace it
        Button lastRetry;

        static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        static readonly Brush DimText = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        static readonly Brush ErrorText = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0xB4));
        static readonly Brush Selection = new SolidColorBrush(Color.FromArgb(0x88, 0xA0, 0xC8, 0xFF));

        // ---------------- welcome ----------------

        void BuildWelcome()
        {
            int h = DateTime.Now.Hour;
            greeting.Text = h < 5 ? "Up late?" : h < 12 ? "Good morning" : h < 17 ? "Good afternoon" : "Good evening";
            var ideas = new[]
            {
                new[] { "", "Summarize something", "Summarize this in a few bullet points:\n" },
                new[] { "", "Explain a concept", "Explain this simply, like I'm new to it: " },
                new[] { "", "Draft a message", "Write a short, friendly message that " },
                new[] { "", "Debug some code", "Help me find the bug in this code:\n" },
            };
            suggestions.Children.Clear();
            foreach (var idea in ideas)
            {
                string prompt = idea[2];
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = G(IdeaIcons[Array.IndexOf(ideas, idea)]), FontFamily = IconFont, FontSize = 12, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 });
                row.Children.Add(new TextBlock { Text = idea[1], VerticalAlignment = VerticalAlignment.Center });
                var b = new Button { Style = (Style)root.FindResource("PillBtn"), Content = row, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 8, 14, 8) };
                b.Click += delegate
                {
                    inputBox.Text = prompt;
                    inputBox.CaretIndex = prompt.Length;
                    InputFocus();
                };
                suggestions.Children.Add(b);
            }
        }

        // ---------------- input ----------------

        void OnInputKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                int i = inputBox.CaretIndex;
                inputBox.Text = inputBox.Text.Insert(i, Environment.NewLine);
                inputBox.CaretIndex = i + Environment.NewLine.Length;
            }
            else Send();
            e.Handled = true;
        }

        void UpdateSendButton()
        {
            bool streaming = streamCts != null;
            sendBtn.Content = streaming ? StopGlyph : SendGlyph;
            sendBtn.ToolTip = streaming ? "Stop" : "Send (Enter)";
            sendBtn.IsEnabled = streaming || inputBox.Text.Trim().Length > 0;
        }

        void NewChat()
        {
            if (streamCts != null) streamCts.Cancel();
            session = new ChatSession();
            messages.Children.Clear();
            lastReply = null; lastRetry = null;
            BuildWelcome();
            empty.Visibility = Visibility.Visible;
            ShowSettings(false);
            UpdateTitle();
            RefreshChatList();
        }

        void PersistSession()
        {
            if (session.Messages.Count == 0) return;
            if (session.Title == "New chat")
            {
                var first = session.Messages.Find(m => m.Role == "user");
                if (first != null) session.Title = ChatStore.MakeTitle(first.Content);
            }
            session.Provider = settings.Provider;
            session.Model = settings.Model;
            ChatStore.Save(session);
            allChats.Remove(session);
            allChats.Insert(0, session);
            UpdateTitle();
            RefreshChatList();
        }

        void UpdateTitle()
        {
            F<TextBlock>("Title").Text = session.Messages.Count > 0 ? session.Title : "Glass";
        }

        void Send()
        {
            if (streamCts != null) { streamCts.Cancel(); return; } // button doubles as Stop
            string text = inputBox.Text.Trim();
            if (text.Length == 0) return;
            if (string.IsNullOrEmpty(settings.ApiKey)) { ShowSettings(true); keyStatus.Text = "Add an API key first."; return; }

            inputBox.Text = "";
            empty.Visibility = Visibility.Collapsed;
            if (lastRetry != null) lastRetry.Visibility = Visibility.Collapsed; // only the latest reply can be retried
            history.Add(new ChatMessage("user", text));
            AddUserMessage(text);
            PersistSession();
            RunReply();
        }

        void Retry()
        {
            if (streamCts != null) return;
            if (history.Count > 0 && history[history.Count - 1].Role == "assistant") history.RemoveAt(history.Count - 1);
            if (lastReply != null) messages.Children.Remove(lastReply);
            if (history.Count == 0 || history[history.Count - 1].Role != "user") return;
            RunReply();
        }

        // ---------------- streaming a reply ----------------

        async void RunReply()
        {
            RichTextBox body; FrameworkElement typing; StackPanel actions; Button copyBtn, retryBtn;
            var view = AddAssistantMessage(settings.Provider, settings.Model, out body, out typing, out actions, out copyBtn, out retryBtn);
            var replySession = session; // the user may switch chats mid-stream
            lastReply = view; lastRetry = retryBtn;
            ScrollToEnd(true);

            var cts = new CancellationTokenSource();
            streamCts = cts;
            UpdateSendButton();

            var sb = new System.Text.StringBuilder();
            var renderClock = Stopwatch.StartNew();
            Exception error = null;
            try
            {
                await AiClient.StreamAsync(settings, history, delegate (string d)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        sb.Append(d);
                        if (typing.Visibility == Visibility.Visible) { typing.Visibility = Visibility.Collapsed; body.Visibility = Visibility.Visible; }
                        if (renderClock.ElapsedMilliseconds > 45) // re-render Markdown ~20x/s while streaming
                        {
                            bool follow = NearBottom();
                            body.Document = Markdown.Render(sb.ToString());
                            renderClock.Restart();
                            if (follow) ScrollToEnd(false);
                        }
                    }));
                }, cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { error = ex; }   // C# 5: no await inside catch, so handled below

            await Dispatcher.InvokeAsync(new Action(delegate { })); // flush pending deltas
            bool stuck = NearBottom();
            typing.Visibility = Visibility.Collapsed;
            body.Visibility = Visibility.Visible;

            string reply = sb.ToString();
            if (reply.Length > 0)
            {
                replySession.Messages.Add(new ChatMessage("assistant", reply));
                if (replySession == session) PersistSession(); else ChatStore.Save(replySession);
            }

            string shown = reply;
            if (cts.IsCancellationRequested) shown += reply.Length > 0 ? "\n\n*Stopped.*" : "*Stopped.*";
            var doc = Markdown.Render(shown);
            if (error != null)
            {
                var p = new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("⚠  " + error.Message))
                {
                    Foreground = ErrorText, Margin = new Thickness(0, reply.Length > 0 ? 8 : 0, 0, 0)
                };
                doc.Blocks.Add(p);
            }
            body.Document = doc;

            copyBtn.Click += delegate
            {
                try { Clipboard.SetText(reply.Length > 0 ? reply : shown); } catch { }
                copyBtn.Content = CheckGlyph;
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
                t.Tick += delegate { copyBtn.Content = CopyGlyph; t.Stop(); };
                t.Start();
            };
            copyBtn.Visibility = reply.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            actions.Visibility = Visibility.Visible;

            streamCts = null;
            UpdateSendButton();
            if (stuck) ScrollToEnd(false);
        }

        bool NearBottom() { return chatScroll.VerticalOffset >= chatScroll.ScrollableHeight - 60; }

        void ScrollToEnd(bool force)
        {
            Dispatcher.BeginInvoke(new Action(delegate { chatScroll.ScrollToEnd(); }), DispatcherPriority.Background);
        }

        // ---------------- message views ----------------

        void AddUserMessage(string text)
        {
            var tb = new TextBox
            {
                Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = White, FontSize = 14, Padding = new Thickness(0), FontFamily = UiFont,
                CaretBrush = Brushes.Transparent, SelectionBrush = Selection, Cursor = Cursors.IBeam
            };
            var bubble = new Border
            {
                Child = tb,
                CornerRadius = new CornerRadius(20, 20, 6, 20),
                Padding = new Thickness(15, 10, 15, 11),
                Margin = new Thickness(44, 10, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromArgb(0x3A, 0xFF, 0xFF, 0xFF)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1)
            };
            messages.Children.Add(bubble);
            Appear(bubble);
        }

        FrameworkElement AddAssistantMessage(string provider, string model, out RichTextBox body, out FrameworkElement typing, out StackPanel actions,
                                             out Button copyBtn, out Button retryBtn)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 6) };

            // avatar + name
            var who = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            who.Children.Add(new Ellipse { Width = 20, Height = 20, Fill = (Brush)root.FindResource("Orb"), Margin = new Thickness(0, 0, 9, 0) });
            who.Children.Add(new TextBlock { Text = ProviderLabel(provider), Foreground = White, FontWeight = FontWeights.SemiBold, FontSize = 13, FontFamily = UiFont, VerticalAlignment = VerticalAlignment.Center });
            who.Children.Add(new TextBlock { Text = "  " + model, Foreground = DimText, FontSize = 11.5, FontFamily = UiFont, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 200 });
            panel.Children.Add(who);

            body = new RichTextBox
            {
                IsReadOnly = true, IsDocumentEnabled = true, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = White, Padding = new Thickness(0), Margin = new Thickness(25, 0, 4, 0),
                CaretBrush = Brushes.Transparent, SelectionBrush = Selection, Visibility = Visibility.Collapsed,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Cursor = Cursors.IBeam
            };
            body.Document = new System.Windows.Documents.FlowDocument();
            panel.Children.Add(body);

            typing = TypingDots();
            panel.Children.Add(typing);

            actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(21, 4, 0, 0), Visibility = Visibility.Collapsed };
            copyBtn = new Button { Style = (Style)root.FindResource("ActionBtn"), Content = CopyGlyph, ToolTip = "Copy" };
            retryBtn = new Button { Style = (Style)root.FindResource("ActionBtn"), Content = RetryGlyph, ToolTip = "Retry" };
            retryBtn.Click += delegate { Retry(); };
            actions.Children.Add(copyBtn);
            actions.Children.Add(retryBtn);
            panel.Children.Add(actions);

            messages.Children.Add(panel);
            Appear(panel);
            return panel;
        }

        // three softly pulsing dots
        FrameworkElement TypingDots()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(29, 6, 0, 4) };
            for (int i = 0; i < 3; i++)
            {
                var dot = new Ellipse { Width = 7, Height = 7, Fill = White, Margin = new Thickness(0, 0, 5, 0), Opacity = 0.25 };
                var anim = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(420))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(i * 160),
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                dot.BeginAnimation(OpacityProperty, anim);
                row.Children.Add(dot);
            }
            return row;
        }

        // messages fade + rise in
        static void Appear(FrameworkElement el)
        {
            var shift = new TranslateTransform(0, 10);
            el.RenderTransform = shift;
            el.Opacity = 0;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease });
        }
    }
}
