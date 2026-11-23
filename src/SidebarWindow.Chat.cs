using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GlassSidebar
{
    // Settings screen + wiring. The conversation UI lives in SidebarWindow.Messages.cs.
    internal sealed partial class SidebarWindow
    {
        public Settings settings = Settings.Load();
        public event Action SettingsChanged;

        StackPanel messages;
        ScrollViewer chatScroll, settingsView;
        TextBox inputBox, modelBox, baseUrlBox, systemBox;
        PasswordBox keyBox;
        TextBlock placeholder, keyStatus, emptyHint, modelChipText, subtitle, greeting;
        WrapPanel suggestions;
        FrameworkElement empty;
        Button sendBtn;
        string draftProvider;
        int draftSens;

        static readonly Brush White = Brushes.White;
        static readonly Brush PillOn = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        static readonly Brush PillOff = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

        bool InSettings { get { return settingsView.Visibility == Visibility.Visible; } }

        void InitChat()
        {
            messages = F<StackPanel>("Messages");
            chatScroll = F<ScrollViewer>("ChatScroll");
            settingsView = F<ScrollViewer>("SettingsView");
            inputBox = F<TextBox>("InputBox");
            modelBox = F<TextBox>("ModelBox");
            baseUrlBox = F<TextBox>("BaseUrlBox");
            systemBox = F<TextBox>("SystemBox");
            keyBox = F<PasswordBox>("KeyBox");
            placeholder = F<TextBlock>("Placeholder");
            keyStatus = F<TextBlock>("KeyStatus");
            emptyHint = F<TextBlock>("EmptyHint");
            modelChipText = F<TextBlock>("ModelChipText");
            subtitle = F<TextBlock>("Subtitle");
            greeting = F<TextBlock>("Greeting");
            suggestions = F<WrapPanel>("Suggestions");
            empty = F<FrameworkElement>("Empty");
            sendBtn = F<Button>("SendBtn");

            inputBox.TextChanged += delegate
            {
                placeholder.Visibility = inputBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateSendButton();
            };
            inputBox.PreviewKeyDown += OnInputKey;
            sendBtn.Click += delegate { Send(); };
            F<Button>("ModelChip").Click += delegate { ShowSettings(true); };
            F<Button>("NewBtn").Click += delegate { NewChat(); };
            F<Button>("KeyBtn").Click += delegate { ShowSettings(!InSettings); };
            F<Button>("BackBtn").Click += delegate { ShowSettings(false); };
            F<Button>("SaveBtn").Click += delegate { SaveSettings(); };
            F<Button>("ImportBtn").Click += delegate { ImportKeyFromFile(); };
            F<Button>("PasteBtn").Click += delegate
            {
                string k = Settings.ExtractKey(Clipboard.ContainsText() ? Clipboard.GetText() : "");
                if (k == null) { keyStatus.Text = "Clipboard doesn't contain an API key."; return; }
                AcceptKey(k, "Pasted from clipboard.");
            };
            F<Button>("ClearKeyBtn").Click += delegate { keyBox.Password = ""; keyStatus.Text = "Key will be removed when you save."; };
            F<Button>("ProvAnthropic").Click += delegate { SetProvider("anthropic", true); };
            F<Button>("ProvOpenAI").Click += delegate { SetProvider("openai", true); };
            F<Button>("ProvGemini").Click += delegate { SetProvider("gemini", true); };
            F<Button>("ProvGroq").Click += delegate { SetProvider("groq", true); };
            F<Button>("ModelsRefresh").Click += delegate { LoadModels(); };
            modelBox.TextChanged += delegate { HighlightModel(); };
            F<Button>("Sens0").Click += delegate { SetSens(0); };
            F<Button>("Sens1").Click += delegate { SetSens(1); };
            F<Button>("Sens2").Click += delegate { SetSens(2); };

            BuildWelcome();
            InitAppearance();
            InitChatList();
            UpdateEmptyHint();
            UpdateSendButton();
        }

        void InputFocus()
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (InSettings) keyBox.Focus(); else { inputBox.Focus(); Keyboard.Focus(inputBox); }
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        static string ProviderLabel(string p)
        {
            return p == "anthropic" ? "Claude" : p == "gemini" ? "Gemini" : p == "groq" ? "Groq" : "Assistant";
        }

        void UpdateEmptyHint()
        {
            bool hasKey = !string.IsNullOrEmpty(settings.ApiKey);
            emptyHint.Text = hasKey
                ? "Tip: flick your cursor into a screen edge any time to summon me."
                : "Add an API key in settings (the gear above) to get started.";
            modelChipText.Text = hasKey ? settings.Model : "Add API key";
            subtitle.Text = hasKey ? ProviderLabel(settings.Provider) + " · " + settings.Model : "Not connected";
        }

        // ---------------- settings ----------------

        void ShowSettings(bool show)
        {
            if (show)
            {
                SyncAppearanceControls();
                keyBox.Password = settings.ApiKey;
                modelBox.Text = settings.Model;
                baseUrlBox.Text = settings.BaseUrl;
                systemBox.Text = settings.SystemPrompt;
                keyStatus.Text = string.IsNullOrEmpty(settings.ApiKey) ? "No key saved yet." : "Key saved (encrypted).";
                SetProvider(settings.Provider, false);
                SetSens(settings.Sensitivity);
            }
            settingsView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            chatView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            composer.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            InputFocus();
        }

        void SetProvider(string p, bool swapModel)
        {
            if (swapModel && p != draftProvider)
                modelBox.Text = p == "anthropic" ? "claude-sonnet-5-5" : (p == "gemini" ? AiClient.GeminiDefaultModel : "");
            draftProvider = p;
            F<Button>("ProvAnthropic").Background = p == "anthropic" ? PillOn : PillOff;
            F<Button>("ProvGemini").Background = p == "gemini" ? PillOn : PillOff;
            F<Button>("ProvGroq").Background = p == "groq" ? PillOn : PillOff;
            F<Button>("ProvOpenAI").Background = p == "openai" ? PillOn : PillOff;
            F<FrameworkElement>("BaseUrlPanel").Visibility = p == "openai" ? Visibility.Visible : Visibility.Collapsed;
            bool picker = p == "gemini" || p == "groq";
            F<FrameworkElement>("ModelsPanel").Visibility = picker ? Visibility.Visible : Visibility.Collapsed;
            if (picker)
            {
                F<TextBlock>("ModelsTitle").Text = p == "gemini" ? "GEMINI MODELS" : "GROQ MODELS";
                if (modelsLoadedFor != p) LoadModels();
            }
        }

        // ---------------- live model picker (Gemini, Groq) ----------------

        string modelsLoadedFor;
        bool modelsLoading;

        async void LoadModels()
        {
            string p = draftProvider;
            string label = p == "gemini" ? "Gemini" : "Groq";
            var status = F<TextBlock>("ModelsStatus");
            var wrap = F<WrapPanel>("ModelsList");
            wrap.Children.Clear();
            modelsLoadedFor = null;
            string key = keyBox.Password.Trim();
            if (key.Length == 0) { status.Text = "Add your " + label + " key above, then hit Refresh to load every model."; return; }
            if (modelsLoading) return;
            modelsLoading = true;
            status.Text = "Loading " + label + " models…";
            try
            {
                var models = p == "gemini" ? await AiClient.ListGeminiModelsAsync(key) : await AiClient.ListGroqModelsAsync(key);
                if (draftProvider != p) return; // user switched provider while loading
                foreach (var m in models)
                {
                    var id = m.Key;
                    var b = new Button { Style = (Style)root.FindResource("PillBtn"), Content = m.Value, ToolTip = id, Margin = new Thickness(0, 0, 6, 6), Tag = id };
                    b.Click += delegate { modelBox.Text = id; HighlightModel(); };
                    wrap.Children.Add(b);
                }
                modelsLoadedFor = p;
                // no model chosen yet (or it isn't on this account): pick the first one
                if (models.Count > 0 && !models.Any(x => x.Key == modelBox.Text.Trim()) && (p == "groq" || modelBox.Text.Trim().Length == 0))
                    modelBox.Text = models[0].Key;
                status.Text = models.Count + " models available. Tap one to use it.";
                HighlightModel();
            }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { modelsLoading = false; }
        }

        void HighlightModel()
        {
            foreach (var c in F<WrapPanel>("ModelsList").Children)
            {
                var b = c as Button;
                if (b != null) b.Background = (b.Tag as string) == modelBox.Text.Trim() ? PillOn : PillOff;
            }
        }

        void SetSens(int s)
        {
            draftSens = s;
            for (int i = 0; i < 3; i++) F<Button>("Sens" + i).Background = i == s ? PillOn : PillOff;
        }

        void AcceptKey(string key, string how)
        {
            keyBox.Password = key;
            // auto-pick the provider from the key's shape
            if (key.StartsWith("sk-ant-")) SetProvider("anthropic", true);
            else if (key.StartsWith("AIza")) { modelsLoadedFor = null; SetProvider("gemini", true); }
            else if (key.StartsWith("gsk_")) { modelsLoadedFor = null; SetProvider("groq", true); }
            else if (draftProvider != "openai" && key.StartsWith("sk-")) SetProvider("openai", true);
            string name = draftProvider == "anthropic" ? "an Anthropic" : draftProvider == "gemini" ? "a Gemini" :
                          draftProvider == "groq" ? "a Groq" : "an OpenAI-compatible";
            keyStatus.Text = how + " Looks like " + name + " key — hit Save.";
        }

        void ImportKeyFromFile()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Import API key";
            dlg.Filter = "Key files (*.txt;*.env;*.json;*.key)|*.txt;*.env;*.json;*.key|All files (*.*)|*.*";
            suppressDeactivate = true;
            bool? ok;
            try { ok = dlg.ShowDialog(this); }
            finally { suppressDeactivate = false; }
            if (ok != true) return;
            try
            {
                string k = Settings.ExtractKey(File.ReadAllText(dlg.FileName));
                if (k == null) { keyStatus.Text = "Couldn't find an API key in that file."; return; }
                AcceptKey(k, "Imported from " + Path.GetFileName(dlg.FileName) + ".");
            }
            catch (Exception ex) { keyStatus.Text = "Couldn't read file: " + ex.Message; }
            Activate();
        }

        void SaveSettings()
        {
            string model = modelBox.Text.Trim();
            if (model.Length == 0) { keyStatus.Text = "Enter a model name (e.g. the one your provider lists)."; modelBox.Focus(); return; }
            settings.ApiKey = keyBox.Password.Trim();
            settings.Provider = draftProvider ?? "anthropic";
            settings.Model = model;
            settings.BaseUrl = baseUrlBox.Text.Trim().Length > 0 ? baseUrlBox.Text.Trim() : "https://api.openai.com/v1";
            settings.SystemPrompt = systemBox.Text;
            settings.Sensitivity = draftSens;
            try { settings.Save(); }
            catch (Exception ex) { keyStatus.Text = "Couldn't save: " + ex.Message; return; }
            var h = SettingsChanged; if (h != null) h();
            UpdateEmptyHint();
            ShowSettings(false);
        }
    }
}
