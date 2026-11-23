using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace GlassSidebar
{
    // Small, forgiving Markdown -> FlowDocument renderer for chat replies.
    // Handles headings, bullet/numbered lists, quotes, fenced code, **bold**, *italic*, `code`, [links](url).
    internal static class Markdown
    {
        static readonly FontFamily Body = new FontFamily("Segoe UI Variable Text, Segoe UI");
        static readonly FontFamily Mono = new FontFamily("Cascadia Code, Cascadia Mono, Consolas");
        static readonly Brush Text = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF));
        static readonly Brush Dim = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));
        static readonly Brush CodeBg = new SolidColorBrush(Color.FromArgb(0x4D, 0x00, 0x00, 0x08));
        static readonly Brush InlineCodeBg = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
        static readonly Brush Link = new SolidColorBrush(Color.FromRgb(0xA8, 0xD8, 0xFF));
        static readonly Regex Inline = new Regex(@"(\*\*(.+?)\*\*)|(`([^`]+)`)|(\[([^\]]+)\]\((https?://[^)\s]+)\))|((?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*]))", RegexOptions.Compiled);

        public static FlowDocument Render(string md)
        {
            var doc = new FlowDocument
            {
                FontFamily = Body, FontSize = 14, Foreground = Text, PagePadding = new Thickness(0),
                LineHeight = 21, TextAlignment = TextAlignment.Left
            };
            string[] lines = (md ?? "").Replace("\r\n", "\n").Split('\n');
            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i];
                string t = line.TrimStart();

                if (t.StartsWith("```"))
                {
                    string lang = t.Substring(3).Trim();
                    var code = new System.Text.StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    {
                        if (code.Length > 0) code.Append('\n');
                        code.Append(lines[i]); i++;
                    }
                    i++; // closing fence (or end while streaming)
                    doc.Blocks.Add(CodeBlock(code.ToString(), lang));
                    continue;
                }

                if (t.Length == 0) { i++; continue; }

                var h = Regex.Match(t, @"^(#{1,6})\s+(.*)$");
                if (h.Success)
                {
                    int level = h.Groups[1].Length;
                    var p = Para(h.Groups[2].Value);
                    p.FontWeight = FontWeights.SemiBold;
                    p.FontSize = level == 1 ? 19 : level == 2 ? 17 : 15;
                    p.Margin = new Thickness(0, 10, 0, 4);
                    doc.Blocks.Add(p); i++; continue;
                }

                if (Regex.IsMatch(t, @"^([-*_])\1{2,}$"))
                {
                    var rule = new Paragraph { Margin = new Thickness(0, 8, 0, 8), BorderBrush = Dim, BorderThickness = new Thickness(0, 0, 0, 0.5), FontSize = 2 };
                    doc.Blocks.Add(rule); i++; continue;
                }

                var bullet = Regex.Match(line, @"^(\s*)([-*+]|\d+[.)])\s+(.*)$");
                if (bullet.Success)
                {
                    int indent = bullet.Groups[1].Value.Replace("\t", "    ").Length / 2;
                    string marker = char.IsDigit(bullet.Groups[2].Value[0]) ? bullet.Groups[2].Value.TrimEnd(')', '.') + "." : "•";
                    var p = new Paragraph { Margin = new Thickness(6 + indent * 16, 2, 0, 2), TextIndent = 0 };
                    p.Inlines.Add(new Run(marker + "  ") { Foreground = Dim });
                    AddInlines(p.Inlines, bullet.Groups[3].Value);
                    doc.Blocks.Add(p); i++; continue;
                }

                if (t.StartsWith(">"))
                {
                    var p = Para(t.TrimStart('>', ' '));
                    p.Foreground = Dim;
                    p.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
                    p.BorderThickness = new Thickness(2, 0, 0, 0);
                    p.Padding = new Thickness(10, 0, 0, 0);
                    p.Margin = new Thickness(0, 4, 0, 4);
                    doc.Blocks.Add(p); i++; continue;
                }

                // plain paragraph: merge consecutive text lines
                var para = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
                bool first = true;
                while (i < lines.Length)
                {
                    string l = lines[i], lt = l.TrimStart();
                    if (lt.Length == 0 || lt.StartsWith("```") || lt.StartsWith("#") || lt.StartsWith(">") ||
                        Regex.IsMatch(l, @"^\s*([-*+]|\d+[.)])\s+")) break;
                    if (!first) para.Inlines.Add(new LineBreak());
                    AddInlines(para.Inlines, l);
                    first = false; i++;
                }
                doc.Blocks.Add(para);
            }
            if (doc.Blocks.Count > 0) doc.Blocks.LastBlock.Margin = new Thickness(doc.Blocks.LastBlock.Margin.Left, doc.Blocks.LastBlock.Margin.Top, 0, 0);
            return doc;
        }

        static Paragraph Para(string text)
        {
            var p = new Paragraph();
            AddInlines(p.Inlines, text);
            return p;
        }

        static Block CodeBlock(string code, string lang)
        {
            var section = new Section
            {
                Background = CodeBg, Margin = new Thickness(0, 4, 0, 12), Padding = new Thickness(12, 8, 12, 10),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1)
            };
            if (lang.Length > 0)
                section.Blocks.Add(new Paragraph(new Run(lang)) { FontSize = 11, Foreground = Dim, Margin = new Thickness(0, 0, 0, 6) });
            var p = new Paragraph { FontFamily = Mono, FontSize = 12.5, LineHeight = 18, Margin = new Thickness(0) };
            string[] cl = code.Split('\n');
            for (int k = 0; k < cl.Length; k++)
            {
                if (k > 0) p.Inlines.Add(new LineBreak());
                p.Inlines.Add(new Run(cl[k]));
            }
            section.Blocks.Add(p);
            return section;
        }

        static void AddInlines(InlineCollection into, string text)
        {
            int pos = 0;
            foreach (Match m in Inline.Matches(text))
            {
                if (m.Index > pos) into.Add(new Run(text.Substring(pos, m.Index - pos)));
                if (m.Groups[1].Success) into.Add(new Bold(new Run(m.Groups[2].Value)));
                else if (m.Groups[3].Success)
                    into.Add(new Run(" " + m.Groups[4].Value + " ") { FontFamily = Mono, FontSize = 12.5, Background = InlineCodeBg });
                else if (m.Groups[5].Success)
                {
                    var link = new Hyperlink(new Run(m.Groups[6].Value)) { Foreground = Link, Cursor = System.Windows.Input.Cursors.Hand };
                    string url = m.Groups[7].Value;
                    link.Click += delegate { try { System.Diagnostics.Process.Start(url); } catch { } };
                    into.Add(link);
                }
                else if (m.Groups[8].Success) into.Add(new Italic(new Run(m.Groups[9].Value)));
                pos = m.Index + m.Length;
            }
            if (pos < text.Length) into.Add(new Run(text.Substring(pos)));
        }
    }
}
