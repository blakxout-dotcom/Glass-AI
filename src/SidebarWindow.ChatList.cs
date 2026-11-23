using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GlassSidebar
{
    // Saved-chats pane (shown in fullscreen): new chat, search, switch, delete.
    internal sealed partial class SidebarWindow
    {
        StackPanel chatList;
        TextBox chatSearch;
        static readonly Brush ActiveItem = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

        void InitChatList()
        {
            chatList = F<StackPanel>("ChatList");
            chatSearch = F<TextBox>("ChatSearch");
            allChats = ChatStore.LoadAll();

            F<Button>("PaneNewChat").Click += delegate { NewChat(); InputFocus(); };
            chatSearch.TextChanged += delegate
            {
                F<TextBlock>("ChatSearchHint").Visibility = chatSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                RefreshChatList();
            };
            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { NewChat(); InputFocus(); e.Handled = true; }
            };
            RefreshChatList();
        }

        void ShowChatsPane(bool show)
        {
            F<FrameworkElement>("ChatsPane").Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            F<FrameworkElement>("HeaderOrb").Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            if (show) RefreshChatList();
        }

        void RefreshChatList()
        {
            if (chatList == null) return;
            chatList.Children.Clear();
            string q = chatSearch.Text.Trim();
            var shown = allChats.Where(c => q.Length == 0 ||
                c.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                c.Messages.Any(m => m.Content != null && m.Content.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            if (shown.Count == 0)
            {
                chatList.Children.Add(new TextBlock
                {
                    Text = q.Length > 0 ? "No chats match." : "Your chats will show up here.",
                    Foreground = DimText, FontSize = 12, Margin = new Thickness(10, 6, 0, 0), TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (var chat in shown)
            {
                var c = chat;
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = c.Title, FontSize = 13, Foreground = White, TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = UiFont });
                text.Children.Add(new TextBlock { Text = ChatStore.RelativeTime(c.Updated) + " · " + ProviderLabel(c.Provider), FontSize = 11, Foreground = DimText, Margin = new Thickness(0, 2, 0, 0), FontFamily = UiFont });
                grid.Children.Add(text);

                var del = new Button { Style = (Style)root.FindResource("ActionBtn"), Content = G(0xE74D), ToolTip = "Delete chat", Visibility = Visibility.Hidden, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(del, 1);
                grid.Children.Add(del);

                var item = new Button { Style = (Style)root.FindResource("ListItem"), Content = grid };
                if (c == session) item.Background = ActiveItem;
                item.MouseEnter += delegate { del.Visibility = Visibility.Visible; };
                item.MouseLeave += delegate { del.Visibility = Visibility.Hidden; };
                item.Click += delegate { LoadSession(c); };
                del.Click += delegate (object s, RoutedEventArgs e)
                {
                    e.Handled = true; // don't also open it
                    ChatStore.Delete(c.Id);
                    allChats.Remove(c);
                    if (c == session) NewChat(); else RefreshChatList();
                };
                chatList.Children.Add(item);
            }
        }

        void LoadSession(ChatSession s)
        {
            if (s == session) return;
            if (streamCts != null) streamCts.Cancel();
            session = s;
            messages.Children.Clear();
            lastReply = null; lastRetry = null;
            ShowSettings(false);

            for (int i = 0; i < s.Messages.Count; i++)
            {
                var m = s.Messages[i];
                if (m.Role == "user") { AddUserMessage(m.Content); continue; }

                RichTextBox body; FrameworkElement typing; StackPanel actions; Button copyBtn, retryBtn;
                var view = AddAssistantMessage(s.Provider, s.Model, out body, out typing, out actions, out copyBtn, out retryBtn);
                typing.Visibility = Visibility.Collapsed;
                body.Visibility = Visibility.Visible;
                body.Document = Markdown.Render(m.Content);
                string content = m.Content;
                var copy = copyBtn;
                copy.Click += delegate { try { Clipboard.SetText(content); } catch { } copy.Content = CheckGlyph; };
                bool isLast = i == s.Messages.Count - 1;
                retryBtn.Visibility = isLast ? Visibility.Visible : Visibility.Collapsed;
                actions.Visibility = Visibility.Visible;
                if (isLast) { lastReply = view; lastRetry = retryBtn; }
            }

            empty.Visibility = s.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateTitle();
            RefreshChatList();
            ScrollToEnd(true);
            InputFocus();
        }
    }
}
