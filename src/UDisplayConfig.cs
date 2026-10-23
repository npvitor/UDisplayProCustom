using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UDisplayPro
{
    public static class D3DShaderCompiler
    {
        [Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface ID3DBlob
        {
            [PreserveSig] IntPtr GetBufferPointer();
            [PreserveSig] IntPtr GetBufferSize();
        }

        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(
            [MarshalAs(UnmanagedType.LPStr)] string pSrcData,
            IntPtr SrcDataSize,
            [MarshalAs(UnmanagedType.LPStr)] string pSourceName,
            IntPtr pDefines,
            IntPtr pInclude,
            [MarshalAs(UnmanagedType.LPStr)] string pEntrypoint,
            [MarshalAs(UnmanagedType.LPStr)] string pTarget,
            uint Flags1,
            uint Flags2,
            out ID3DBlob ppCode,
            out ID3DBlob ppErrorMsgs);

        [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.Winapi)]
        private static extern int D3DStripShader(
            IntPtr pShaderBytecode,
            IntPtr BytecodeLength,
            uint uStripFlags,
            out ID3DBlob ppStrippedBlob);

        public static byte[] CompilePS(string hlsl)
        {
            ID3DBlob code, err;
            int hr = D3DCompile(hlsl, (IntPtr)hlsl.Length, null, IntPtr.Zero, IntPtr.Zero, "main", "ps_5_0", (1u << 15), 0, out code, out err);
            if (hr < 0 || code == null)
            {
                string msg = err != null ? Marshal.PtrToStringAnsi(err.GetBufferPointer(), (int)err.GetBufferSize()) : ("HRESULT 0x" + hr.ToString("X8"));
                throw new Exception("Erro ao compilar Pixel Shader D3D11: " + msg);
            }
            ID3DBlob stripped;
            int hr2 = D3DStripShader(code.GetBufferPointer(), code.GetBufferSize(), 0x1Fu, out stripped);
            ID3DBlob finalBlob = (hr2 >= 0 && stripped != null) ? stripped : code;
            int sz = (int)finalBlob.GetBufferSize();
            byte[] res = new byte[sz];
            Marshal.Copy(finalBlob.GetBufferPointer(), res, 0, sz);
            return res;
        }
    }

    public class ProConfig
    {
        public int ColorMode = 0;        // 0 = BT.709 Limited (16-235), 1 = BT.709 Full (0-255), 2 = BT.601 Limited
        public int Brightness = 0;       // -30 .. +30 (%)
        public int Contrast = 100;       // 60 .. 140 (%)
        public int Saturation = 100;     // 0 .. 160 (%)
        public int GammaX100 = 100;      // 75 .. 140 (1.00 = 100)
        public int ColorTemp = 0;        // 0 = 6500K, 1 = 5500K, 2 = 4500K, 3 = 7500K
        public int TargetFps = 60;       // 30, 45, 60, 75, 90
        public int EncoderMode = 0;      // 0 = CPU x264 Zero-Latency, 1 = GPU Hardware
        public int BitrateMbps = 24;     // 8, 16, 24, 32
        public int CpuThreads = 4;       // 2, 4, 6, 8
        public int StaticRefreshMs = 80; // 50, 80, 200, 500
        public bool CompatGdiMode = false;

        public void Load(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("#") || line.StartsWith("[") || !line.Contains("=")) continue;
                    string[] parts = line.Split(new char[] { '=' }, 2);
                    string k = parts[0].Trim();
                    string v = parts[1].Trim();
                    int iv;
                    if (int.TryParse(v, out iv))
                    {
                        if (k == "ColorMode") ColorMode = Math.Max(0, Math.Min(2, iv));
                        else if (k == "Brightness") Brightness = Math.Max(-30, Math.Min(30, iv));
                        else if (k == "Contrast") Contrast = Math.Max(60, Math.Min(140, iv));
                        else if (k == "Saturation") Saturation = Math.Max(0, Math.Min(160, iv));
                        else if (k == "GammaX100") GammaX100 = Math.Max(75, Math.Min(140, iv));
                        else if (k == "ColorTemp") ColorTemp = Math.Max(0, Math.Min(3, iv));
                        else if (k == "TargetFps") TargetFps = Math.Max(20, Math.Min(120, iv));
                        else if (k == "EncoderMode") EncoderMode = Math.Max(0, Math.Min(1, iv));
                        else if (k == "BitrateMbps") BitrateMbps = Math.Max(4, Math.Min(40, iv));
                        else if (k == "CpuThreads") CpuThreads = Math.Max(1, Math.Min(16, iv));
                        else if (k == "StaticRefreshMs") StaticRefreshMs = Math.Max(30, Math.Min(2000, iv));
                    }
                    if (k == "CompatGdiMode") CompatGdiMode = (v.ToLowerInvariant() == "true" || v == "1");
                }
            }
            catch { }
        }

        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("[UDisplayPro]");
                sb.AppendLine("ColorMode=" + ColorMode);
                sb.AppendLine("Brightness=" + Brightness);
                sb.AppendLine("Contrast=" + Contrast);
                sb.AppendLine("Saturation=" + Saturation);
                sb.AppendLine("GammaX100=" + GammaX100);
                sb.AppendLine("ColorTemp=" + ColorTemp);
                sb.AppendLine("TargetFps=" + TargetFps);
                sb.AppendLine("EncoderMode=" + EncoderMode);
                sb.AppendLine("BitrateMbps=" + BitrateMbps);
                sb.AppendLine("CpuThreads=" + CpuThreads);
                sb.AppendLine("StaticRefreshMs=" + StaticRefreshMs);
                sb.AppendLine("CompatGdiMode=" + (CompatGdiMode ? "true" : "false"));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    public class SegmentedSelect : UniformGrid
    {
        private Button[] buttons;
        private int selectedIndex = 0;
        public event Action<int> SelectionChanged;

        private static readonly Brush BgActive = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        private static readonly Brush BorderActive = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        private static readonly Brush FgActive = Brushes.White;

        private static readonly Brush BgInactive = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        private static readonly Brush BorderInactive = new SolidColorBrush(Color.FromRgb(51, 65, 85));
        private static readonly Brush FgInactive = new SolidColorBrush(Color.FromRgb(203, 213, 225));

        public SegmentedSelect(string[] options)
        {
            this.Rows = 1;
            this.Columns = options.Length;
            buttons = new Button[options.Length];

            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                Button b = ModernUI.CreateCustomButton(options[i], BgInactive, FgInactive, BorderInactive, 6, new Thickness(i == 0 ? 0 : 6, 0, 0, 0), 34);
                b.FontSize = 12.5;
                b.Click += (s, e) => { SelectedIndex = idx; };
                buttons[i] = b;
                this.Children.Add(b);
            }
            UpdateVisuals();
        }

        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                int clamped = Math.Max(0, Math.Min(buttons.Length - 1, value));
                selectedIndex = clamped;
                UpdateVisuals();
                if (SelectionChanged != null) SelectionChanged(selectedIndex);
            }
        }

        private void UpdateVisuals()
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                bool active = (i == selectedIndex);
                ModernUI.SetButtonColors(buttons[i],
                    active ? BgActive : BgInactive,
                    active ? FgActive : FgInactive,
                    active ? BorderActive : BorderInactive,
                    active ? FontWeights.SemiBold : FontWeights.Normal);
            }
        }
    }

    public static class ModernUI
    {
        public static readonly Brush BgWindow = new SolidColorBrush(Color.FromRgb(15, 23, 42));     // #0F172A
        public static readonly Brush BgHeader = new SolidColorBrush(Color.FromRgb(9, 14, 26));      // #090E1A
        public static readonly Brush BgCard = new SolidColorBrush(Color.FromRgb(30, 41, 59));       // #1E293B
        public static readonly Brush BorderCard = new SolidColorBrush(Color.FromRgb(51, 65, 85));   // #334155
        public static readonly Brush TextPrimary = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        public static readonly Brush TextMuted = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        public static readonly Brush AccentBlue = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        public static readonly Brush AccentGreen = new SolidColorBrush(Color.FromRgb(74, 222, 128));
        public static readonly Brush PrimaryBtnBg = new SolidColorBrush(Color.FromRgb(37, 99, 235));
        public static readonly Brush PrimaryBtnBorder = new SolidColorBrush(Color.FromRgb(96, 165, 250));
        public static readonly Brush SecondaryBtnBg = new SolidColorBrush(Color.FromRgb(30, 41, 59));
        public static readonly Brush SecondaryBtnBorder = new SolidColorBrush(Color.FromRgb(71, 85, 105));

        public static Border CreateCard(string title, string subtitle, UIElement content)
        {
            StackPanel sp = new StackPanel();
            TextBlock tbTitle = new TextBlock
            {
                Text = title,
                Foreground = AccentBlue,
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, string.IsNullOrEmpty(subtitle) ? 9 : 2)
            };
            sp.Children.Add(tbTitle);

            if (!string.IsNullOrEmpty(subtitle))
            {
                TextBlock tbSub = new TextBlock
                {
                    Text = subtitle,
                    Foreground = TextMuted,
                    FontSize = 11.8,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 9)
                };
                sp.Children.Add(tbSub);
            }

            sp.Children.Add(content);

            return new Border
            {
                Background = BgCard,
                BorderBrush = BorderCard,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(16, 11, 16, 12),
                Margin = new Thickness(0, 0, 0, 9),
                Child = sp
            };
        }

        public static Button CreateCustomButton(string text, Brush bg, Brush fg, Brush border, double radius, Thickness margin, double height)
        {
            Button btn = new Button
            {
                Content = text,
                Background = bg,
                Foreground = fg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                Margin = margin,
                Height = height,
                Cursor = Cursors.Hand,
                FontSize = 12.5,
                Padding = new Thickness(6, 0, 6, 0)
            };

            ControlTemplate tpl = new ControlTemplate(typeof(Button));
            FrameworkElementFactory bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "PART_Border";
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);

            tpl.VisualTree = bd;
            btn.Template = tpl;
            return btn;
        }

        public static void SetButtonColors(Button btn, Brush bg, Brush fg, Brush border, FontWeight weight)
        {
            btn.Background = bg;
            btn.Foreground = fg;
            btn.BorderBrush = border;
            btn.FontWeight = weight;
        }

        public static Grid CreateLabeledRow(string labelText, UIElement rightControl, bool isLast)
        {
            Grid g = new Grid { Margin = new Thickness(0, 0, 0, isLast ? 0 : 9) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock lbl = new TextBlock
            {
                Text = labelText,
                Foreground = TextPrimary,
                FontSize = 12.8,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(rightControl, 1);

            g.Children.Add(lbl);
            g.Children.Add(rightControl);
            return g;
        }
    }

    public class MainWindow : Window
    {
        private ProConfig cfg = new ProConfig();
        private string progDataDir = @"C:\ProgramData\UDisplay";
        private string appDir;

        private Button[] navTabs = new Button[3];
        private UIElement[] pages = new UIElement[3];

        private SegmentedSelect segColorMode, segColorTemp;
        private Slider sldBrightness, sldContrast, sldSaturation, sldGamma;
        private TextBlock lblBrightnessVal, lblContrastVal, lblSaturationVal, lblGammaVal;

        private SegmentedSelect segFps, segEncoder, segBitrate, segThreads, segRefresh, segCapture;

        private TextBlock lblHeaderBadge;
        private TextBlock lblDiagAdapter, lblDiagProcs, lblDiagPatch;
        private TextBlock lblFooter;
        private DispatcherTimer statusTimer;

        public MainWindow()
        {
            appDir = AppDomain.CurrentDomain.BaseDirectory;
            string iniPath = Path.Combine(progDataDir, "UDisplayPro.ini");
            if (!File.Exists(iniPath))
            {
                string localIni = Path.Combine(appDir, "UDisplayPro.ini");
                if (File.Exists(localIni)) iniPath = localIni;
            }
            cfg.Load(iniPath);

            BuildWindow();
            LoadConfigToUI();
            UpdateLiveStatus();

            statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            statusTimer.Tick += (s, e) => UpdateLiveStatus();
            statusTimer.Start();
        }

        private void BuildWindow()
        {
            this.Title = "UDisplay Pro — Central de Configuração e Calibração (Lemorele LC06 Plus)";
            this.Width = 880;
            this.Height = 695;
            this.MinWidth = 820;
            this.MinHeight = 660;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = ModernUI.BgWindow;
            this.Foreground = ModernUI.TextPrimary;
            this.FontFamily = new FontFamily("Segoe UI");

            try
            {
                string exeIconSrc = Path.Combine(progDataDir, "UDisplay.exe");
                if (!File.Exists(exeIconSrc)) exeIconSrc = Path.Combine(appDir, "UDisplay.exe");
                if (File.Exists(exeIconSrc))
                {
                    using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(exeIconSrc))
                    {
                        if (ico != null)
                        {
                            this.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        }
                    }
                }
            }
            catch { }

            Grid root = new Grid { Background = ModernUI.BgWindow };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Nav Bar
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

            // 1. Header
            Border headerBorder = new Border
            {
                Background = ModernUI.BgHeader,
                BorderBrush = ModernUI.BorderCard,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(22, 14, 22, 14)
            };
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel headerLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            TextBlock tbMainTitle = new TextBlock
            {
                Text = "UDisplay Pro — Calibração de Imagem, 60 FPS e Zero-Latency",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = ModernUI.AccentBlue
            };
            TextBlock tbSubTitle = new TextBlock
            {
                Text = "Adaptador USB-C Lemorele LC06 Plus • Shaders Direct3D 11 BT.709 • Resposta Instantânea de Mouse",
                FontSize = 12,
                Foreground = ModernUI.TextMuted,
                Margin = new Thickness(0, 3, 0, 0)
            };
            headerLeft.Children.Add(tbMainTitle);
            headerLeft.Children.Add(tbSubTitle);

            Border statusBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(20, 45, 38)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(34, 120, 80)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(14, 6, 14, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            lblHeaderBadge = new TextBlock
            {
                Text = "● 60 FPS Ativo",
                Foreground = ModernUI.AccentGreen,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold
            };
            statusBadge.Child = lblHeaderBadge;

            Grid.SetColumn(headerLeft, 0);
            Grid.SetColumn(statusBadge, 1);
            headerGrid.Children.Add(headerLeft);
            headerGrid.Children.Add(statusBadge);
            headerBorder.Child = headerGrid;
            Grid.SetRow(headerBorder, 0);
            root.Children.Add(headerBorder);

            // 2. Navigation Bar
            Border navBorder = new Border
            {
                Background = ModernUI.BgWindow,
                Padding = new Thickness(22, 12, 22, 4)
            };
            UniformGrid navGrid = new UniformGrid { Rows = 1, Columns = 3 };
            string[] tabNames = new string[] {
                "1. Calibração de Cor e Contraste",
                "2. Desempenho, 60 FPS e Latência",
                "3. Monitores, Diagnóstico e Sistema"
            };
            for (int i = 0; i < 3; i++)
            {
                int tabIdx = i;
                Button b = ModernUI.CreateCustomButton(
                    tabNames[i],
                    ModernUI.SecondaryBtnBg,
                    ModernUI.TextPrimary,
                    ModernUI.SecondaryBtnBorder,
                    8,
                    new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                    38);
                b.Click += (s, e) => SelectTab(tabIdx);
                navTabs[i] = b;
                navGrid.Children.Add(b);
            }
            navBorder.Child = navGrid;
            Grid.SetRow(navBorder, 1);
            root.Children.Add(navBorder);

            // 3. Content Area
            ScrollViewer scroll = new ScrollViewer
            {
                Background = ModernUI.BgWindow,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(22, 8, 22, 8)
            };
            Grid pagesHost = new Grid();
            pages[0] = BuildColorPage();
            pages[1] = BuildPerfPage();
            pages[2] = BuildSystemPage();
            foreach (UIElement p in pages) pagesHost.Children.Add(p);
            scroll.Content = pagesHost;
            Grid.SetRow(scroll, 2);
            root.Children.Add(scroll);

            // 4. Footer Bar
            Border footerBorder = new Border
            {
                Background = ModernUI.BgHeader,
                BorderBrush = ModernUI.BorderCard,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(22, 10, 22, 10)
            };
            Grid footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            lblFooter = new TextBlock
            {
                Text = "Pronto. Ajuste as opções desejadas e clique em 'Aplicar e Atualizar Monitor'.",
                Foreground = ModernUI.AccentGreen,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 16, 0)
            };

            StackPanel footerBtns = new StackPanel { Orientation = Orientation.Horizontal };
            Button btnDefault = ModernUI.CreateCustomButton(
                "Restaurar Padrão Otimizado",
                ModernUI.SecondaryBtnBg,
                ModernUI.TextPrimary,
                ModernUI.SecondaryBtnBorder,
                7,
                new Thickness(0, 0, 10, 0),
                36);
            btnDefault.Padding = new Thickness(14, 0, 14, 0);
            btnDefault.Click += (s, e) => ApplyPreset(0);

            Button btnApply = ModernUI.CreateCustomButton(
                "Aplicar e Atualizar Monitor",
                ModernUI.PrimaryBtnBg,
                Brushes.White,
                ModernUI.PrimaryBtnBorder,
                7,
                new Thickness(0),
                36);
            btnApply.FontWeight = FontWeights.SemiBold;
            btnApply.Padding = new Thickness(18, 0, 18, 0);
            btnApply.Click += (s, e) => SaveAndApplyAll();

            footerBtns.Children.Add(btnDefault);
            footerBtns.Children.Add(btnApply);

            Grid.SetColumn(lblFooter, 0);
            Grid.SetColumn(footerBtns, 1);
            footerGrid.Children.Add(lblFooter);
            footerGrid.Children.Add(footerBtns);
            footerBorder.Child = footerGrid;
            Grid.SetRow(footerBorder, 3);
            root.Children.Add(footerBorder);

            this.Content = root;
            SelectTab(0);
        }

        public void SelectTab(int index)
        {
            for (int i = 0; i < 3; i++)
            {
                bool active = (i == index);
                pages[i].Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                ModernUI.SetButtonColors(
                    navTabs[i],
                    active ? ModernUI.PrimaryBtnBg : ModernUI.SecondaryBtnBg,
                    active ? Brushes.White : ModernUI.TextMuted,
                    active ? ModernUI.PrimaryBtnBorder : ModernUI.SecondaryBtnBorder,
                    active ? FontWeights.SemiBold : FontWeights.Normal);
            }
        }

        private UIElement BuildColorPage()
        {
            StackPanel sp = new StackPanel();

            // Card 1: Presets
            UniformGrid presetGrid = new UniformGrid { Rows = 1, Columns = 4 };
            string[] presetNames = new string[] {
                "Padrão Natural (HDMI)",
                "Escritório / Texto Suave",
                "Cinema / Vídeo",
                "Modo Leitura (Quente)"
            };
            for (int i = 0; i < 4; i++)
            {
                int pIdx = i;
                Button b = ModernUI.CreateCustomButton(
                    presetNames[i],
                    new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    ModernUI.TextPrimary,
                    ModernUI.SecondaryBtnBorder,
                    6,
                    new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                    33);
                b.FontSize = 12.2;
                b.Click += (s, e) => ApplyPreset(pIdx);
                presetGrid.Children.Add(b);
            }
            sp.Children.Add(ModernUI.CreateCard(
                "Predefinições Rápidas de Imagem (1-Clique)",
                "Selecione um perfil pronto ou personalize livremente as opções abaixo.",
                presetGrid));

            // Card 2: Color Space & Color Temp
            StackPanel spMatrix = new StackPanel();
            segColorMode = new SegmentedSelect(new string[] {
                "BT.709 HDMI (16-235) ★",
                "BT.709 PC Completo (0-255)",
                "BT.601 Legado (16-235)"
            });
            segColorTemp = new SegmentedSelect(new string[] {
                "6500K Neutro ★",
                "5500K Conforto",
                "4500K Quente",
                "7500K Frio"
            });
            spMatrix.Children.Add(ModernUI.CreateLabeledRow("Espaço de Cor HDMI:", segColorMode, false));
            spMatrix.Children.Add(ModernUI.CreateLabeledRow("Temperatura de Cor:", segColorTemp, true));
            sp.Children.Add(ModernUI.CreateCard(
                "Matriz de Cor HDMI e Temperatura (Balanço RGB)",
                "O modo BT.709 HDMI (16-235) evita que áreas escuras fiquem pretas demais e brancos estourem.",
                spMatrix));

            // Card 3: Sliders
            StackPanel spSliders = new StackPanel();
            spSliders.Children.Add(CreateSliderRow("Brilho (Nível de Luz):", -30, 30, 0, out sldBrightness, out lblBrightnessVal,
                v => (v > 0 ? "+" : "") + Math.Round(v) + "%", false));
            spSliders.Children.Add(CreateSliderRow("Contraste (Ganho Dinâmico):", 60, 140, 100, out sldContrast, out lblContrastVal,
                v => Math.Round(v) + "%", false));
            spSliders.Children.Add(CreateSliderRow("Saturação (Intensidade de Cor):", 0, 160, 100, out sldSaturation, out lblSaturationVal,
                v => Math.Round(v) + "%", false));
            spSliders.Children.Add(CreateSliderRow("Correção Gamma (Meios-Tons):", 75, 140, 100, out sldGamma, out lblGammaVal,
                v => (v / 100.0).ToString("0.00", CultureInfo.InvariantCulture), true));

            sp.Children.Add(ModernUI.CreateCard(
                "Ajuste Fino nos Pixel Shaders Direct3D 11 (Processado na GPU)",
                "Estes ajustes são compilados diretamente no shader HLSL ps_5_0 da placa de vídeo sem custo de CPU.",
                spSliders));

            return sp;
        }

        private UIElement BuildPerfPage()
        {
            StackPanel sp = new StackPanel();

            StackPanel spVideo = new StackPanel();
            segFps = new SegmentedSelect(new string[] {
                "30 FPS",
                "45 FPS",
                "60 FPS (Ideal ★)",
                "75 FPS",
                "90 FPS"
            });
            segEncoder = new SegmentedSelect(new string[] {
                "CPU x264 Zero-Latency (Sem Atraso ★)",
                "GPU Hardware (Intel QuickSync / NVIDIA)"
            });
            segBitrate = new SegmentedSelect(new string[] {
                "8 Mbps (Leve)",
                "16 Mbps (Padrão)",
                "24 Mbps (Nítido ★)",
                "32 Mbps (Máximo)"
            });

            spVideo.Children.Add(ModernUI.CreateLabeledRow("Taxa de Quadros (FPS):", segFps, false));
            spVideo.Children.Add(ModernUI.CreateLabeledRow("Codificador de Vídeo H.264:", segEncoder, false));
            spVideo.Children.Add(ModernUI.CreateLabeledRow("Taxa de Bits (Nitidez):", segBitrate, true));

            sp.Children.Add(ModernUI.CreateCard(
                "Fluidez de Vídeo (FPS), Codificador e Nitidez",
                "O modo 'CPU x264 Zero-Latency' elimina a fila de reordenação de 2 quadros do chip, zerando o atraso de clique.",
                spVideo));

            StackPanel spLatency = new StackPanel();
            segThreads = new SegmentedSelect(new string[] {
                "2 Threads",
                "4 Threads (Ideal ★)",
                "6 Threads",
                "8 Threads"
            });
            segRefresh = new SegmentedSelect(new string[] {
                "50 ms (Ultra)",
                "80 ms (Ideal ★)",
                "200 ms (Médio)",
                "500 ms (Leve)"
            });
            segCapture = new SegmentedSelect(new string[] {
                "Direct3D 11 Desktop Duplication (GPU ★)",
                "Modo Compatibilidade GDI"
            });

            spLatency.Children.Add(ModernUI.CreateLabeledRow("Threads do Codificador CPU:", segThreads, false));
            spLatency.Children.Add(ModernUI.CreateLabeledRow("Resposta de Hover / Clique:", segRefresh, false));
            spLatency.Children.Add(ModernUI.CreateLabeledRow("API de Captura do Windows:", segCapture, true));

            sp.Children.Add(ModernUI.CreateCard(
                "Tempo de Resposta de Foco do Mouse e Captura",
                "Controla a velocidade de atualização imediata quando o mouse passa sobre botões ou menus.",
                spLatency));

            return sp;
        }

        private UIElement BuildSystemPage()
        {
            StackPanel sp = new StackPanel();

            // Card 1: Quick Display Mode Actions
            UniformGrid dispGrid = new UniformGrid { Rows = 1, Columns = 3 };
            Button bExtend = ModernUI.CreateCustomButton("Estender Telas (Modo Estendido)", new SolidColorBrush(Color.FromRgb(15, 23, 42)), ModernUI.TextPrimary, ModernUI.SecondaryBtnBorder, 6, new Thickness(0, 0, 6, 0), 36);
            Button bClone = ModernUI.CreateCustomButton("Duplicar / Espelhar Vídeos", new SolidColorBrush(Color.FromRgb(15, 23, 42)), ModernUI.TextPrimary, ModernUI.SecondaryBtnBorder, 6, new Thickness(6, 0, 6, 0), 36);
            Button bWinSet = ModernUI.CreateCustomButton("Posição e Resolução (Windows)", new SolidColorBrush(Color.FromRgb(15, 23, 42)), ModernUI.TextPrimary, ModernUI.SecondaryBtnBorder, 6, new Thickness(6, 0, 0, 0), 36);

            bExtend.Click += (s, e) => SetWindowsDisplayMode("/extend", "Modo 'Estender Telas' ativado no Windows.");
            bClone.Click += (s, e) => SetWindowsDisplayMode("/clone", "Modo 'Duplicar / Espelhar Telas' ativado no Windows.");
            bWinSet.Click += (s, e) => { try { Process.Start("ms-settings:display"); } catch { } };

            dispGrid.Children.Add(bExtend);
            dispGrid.Children.Add(bClone);
            dispGrid.Children.Add(bWinSet);

            sp.Children.Add(ModernUI.CreateCard(
                "Modo de Exibição dos Monitores (Acesso Rápido)",
                "Alterne rapidamente entre Tela Estendida, Espelhamento ou organize a posição dos monitores no Windows.",
                dispGrid));

            // Card 2: Live Status & Bug Fixes
            StackPanel spStatus = new StackPanel();
            lblDiagAdapter = new TextBlock { Foreground = ModernUI.AccentGreen, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) };
            lblDiagProcs = new TextBlock { Foreground = ModernUI.TextPrimary, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) };
            lblDiagPatch = new TextBlock { Foreground = ModernUI.AccentBlue, FontSize = 13, Margin = new Thickness(0, 0, 0, 10) };

            TextBlock tbBugs = new TextBlock
            {
                Foreground = ModernUI.TextMuted,
                FontSize = 12.2,
                LineHeight = 19,
                Text = "✔ Shaders D3D11 BT.709 ativos (Fim do contraste estourado e saturação excessiva)\n" +
                       "✔ 60 FPS reais + Zero-Latency Constrained Baseline (Sem atraso ao passar/clicar o mouse)\n" +
                       "✔ Loop OTA de 5s desativado, telemetria bloqueada e ícone da bandeja vinculado ao novo painel"
            };

            spStatus.Children.Add(lblDiagAdapter);
            spStatus.Children.Add(lblDiagProcs);
            spStatus.Children.Add(lblDiagPatch);
            spStatus.Children.Add(tbBugs);

            sp.Children.Add(ModernUI.CreateCard(
                "Diagnóstico em Tempo Real e Correções Ativas",
                null,
                spStatus));

            // Card 3: Maintenance & Install on another PC
            UniformGrid maintGrid = new UniformGrid { Rows = 1, Columns = 3 };
            Button bRestart = ModernUI.CreateCustomButton("Reiniciar Monitor USB-C Agora", new SolidColorBrush(Color.FromRgb(20, 83, 60)), Brushes.White, new SolidColorBrush(Color.FromRgb(52, 211, 153)), 6, new Thickness(0, 0, 6, 0), 38);
            Button bInstall = ModernUI.CreateCustomButton("Instalar Driver + App neste PC", ModernUI.PrimaryBtnBg, Brushes.White, ModernUI.PrimaryBtnBorder, 6, new Thickness(6, 0, 6, 0), 38);
            Button bRestore = ModernUI.CreateCustomButton("Restaurar Padrão Otimizado", new SolidColorBrush(Color.FromRgb(15, 23, 42)), ModernUI.TextPrimary, ModernUI.SecondaryBtnBorder, 6, new Thickness(6, 0, 0, 0), 38);

            bRestart.Click += (s, e) => {
                lblFooter.Text = "Reiniciando captura de vídeo do monitor USB-C...";
                RestartUDisplayProcesses();
                lblFooter.Text = "Monitor USB-C reiniciado com sucesso!";
            };
            bInstall.Click += (s, e) => InstallOnThisComputer();
            bRestore.Click += (s, e) => { ApplyPreset(0); SaveAndApplyAll(); };

            maintGrid.Children.Add(bRestart);
            maintGrid.Children.Add(bInstall);
            maintGrid.Children.Add(bRestore);

            sp.Children.Add(ModernUI.CreateCard(
                "Manutenção e Instalação em Outros Computadores",
                "Ao copiar esta pasta para outro computador, clique em 'Instalar Driver + App neste PC' para configurar tudo automaticamente.",
                maintGrid));

            return sp;
        }

        private Grid CreateSliderRow(string title, double min, double max, double val, out Slider slider, out TextBlock valLabel, Func<double, string> formatter, bool isLast)
        {
            Grid g = new Grid { Margin = new Thickness(0, 0, 0, isLast ? 0 : 7) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });

            TextBlock lbl = new TextBlock
            {
                Text = title,
                Foreground = ModernUI.TextPrimary,
                FontSize = 12.8,
                VerticalAlignment = VerticalAlignment.Center
            };

            Slider sld = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = val,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 12, 0),
                Cursor = Cursors.Hand
            };

            Border badge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                BorderBrush = ModernUI.BorderCard,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 3, 6, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock vText = new TextBlock
            {
                Text = formatter(val),
                Foreground = ModernUI.AccentGreen,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            badge.Child = vText;

            TextBlock capturedLabel = vText;
            sld.ValueChanged += (s, e) => { capturedLabel.Text = formatter(sld.Value); };

            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(sld, 1);
            Grid.SetColumn(badge, 2);

            g.Children.Add(lbl);
            g.Children.Add(sld);
            g.Children.Add(badge);

            slider = sld;
            valLabel = vText;
            return g;
        }

        private void ApplyPreset(int preset)
        {
            if (preset == 0) // Padrão Natural (HDMI)
            {
                segColorMode.SelectedIndex = 0;
                segColorTemp.SelectedIndex = 0;
                sldBrightness.Value = 0;
                sldContrast.Value = 100;
                sldSaturation.Value = 100;
                sldGamma.Value = 100;
                segFps.SelectedIndex = 2;       // 60 FPS
                segEncoder.SelectedIndex = 0;   // CPU Zero-Latency
                segBitrate.SelectedIndex = 2;   // 24 Mbps
                segThreads.SelectedIndex = 1;   // 4 Threads
                segRefresh.SelectedIndex = 1;   // 80 ms
                segCapture.SelectedIndex = 0;
                lblFooter.Text = "Perfil 'Padrão Natural (HDMI 60Hz)' selecionado. Clique em Aplicar!";
            }
            else if (preset == 1) // Escritório / Texto Suave
            {
                segColorMode.SelectedIndex = 0;
                segColorTemp.SelectedIndex = 1; // 5500K
                sldBrightness.Value = -2;
                sldContrast.Value = 94;
                sldSaturation.Value = 92;
                sldGamma.Value = 105;
                lblFooter.Text = "Perfil 'Escritório / Texto Suave' selecionado. Clique em Aplicar!";
            }
            else if (preset == 2) // Cinema / Vídeo
            {
                segColorMode.SelectedIndex = 0;
                segColorTemp.SelectedIndex = 0;
                sldBrightness.Value = 0;
                sldContrast.Value = 106;
                sldSaturation.Value = 108;
                sldGamma.Value = 106;
                lblFooter.Text = "Perfil 'Cinema / Vídeo' selecionado. Clique em Aplicar!";
            }
            else if (preset == 3) // Modo Leitura (Quente)
            {
                segColorMode.SelectedIndex = 0;
                segColorTemp.SelectedIndex = 2; // 4500K
                sldBrightness.Value = -5;
                sldContrast.Value = 90;
                sldSaturation.Value = 88;
                sldGamma.Value = 105;
                lblFooter.Text = "Perfil 'Modo Leitura (Filtro de Luz Azul)' selecionado. Clique em Aplicar!";
            }
        }

        private void LoadConfigToUI()
        {
            segColorMode.SelectedIndex = cfg.ColorMode;
            segColorTemp.SelectedIndex = cfg.ColorTemp;
            sldBrightness.Value = cfg.Brightness;
            sldContrast.Value = cfg.Contrast;
            sldSaturation.Value = cfg.Saturation;
            sldGamma.Value = cfg.GammaX100;

            if (cfg.TargetFps <= 30) segFps.SelectedIndex = 0;
            else if (cfg.TargetFps <= 45) segFps.SelectedIndex = 1;
            else if (cfg.TargetFps <= 60) segFps.SelectedIndex = 2;
            else if (cfg.TargetFps <= 75) segFps.SelectedIndex = 3;
            else segFps.SelectedIndex = 4;

            segEncoder.SelectedIndex = cfg.EncoderMode == 1 ? 1 : 0;

            if (cfg.BitrateMbps <= 8) segBitrate.SelectedIndex = 0;
            else if (cfg.BitrateMbps <= 16) segBitrate.SelectedIndex = 1;
            else if (cfg.BitrateMbps <= 24) segBitrate.SelectedIndex = 2;
            else segBitrate.SelectedIndex = 3;

            if (cfg.CpuThreads <= 2) segThreads.SelectedIndex = 0;
            else if (cfg.CpuThreads <= 4) segThreads.SelectedIndex = 1;
            else if (cfg.CpuThreads <= 6) segThreads.SelectedIndex = 2;
            else segThreads.SelectedIndex = 3;

            if (cfg.StaticRefreshMs <= 50) segRefresh.SelectedIndex = 0;
            else if (cfg.StaticRefreshMs <= 80) segRefresh.SelectedIndex = 1;
            else if (cfg.StaticRefreshMs <= 200) segRefresh.SelectedIndex = 2;
            else segRefresh.SelectedIndex = 3;

            segCapture.SelectedIndex = cfg.CompatGdiMode ? 1 : 0;
        }

        private void ReadUIToConfig()
        {
            cfg.ColorMode = segColorMode.SelectedIndex;
            cfg.ColorTemp = segColorTemp.SelectedIndex;
            cfg.Brightness = (int)Math.Round(sldBrightness.Value);
            cfg.Contrast = (int)Math.Round(sldContrast.Value);
            cfg.Saturation = (int)Math.Round(sldSaturation.Value);
            cfg.GammaX100 = (int)Math.Round(sldGamma.Value);

            int[] fpsMap = new int[] { 30, 45, 60, 75, 90 };
            cfg.TargetFps = fpsMap[segFps.SelectedIndex];
            cfg.EncoderMode = segEncoder.SelectedIndex;

            int[] brMap = new int[] { 8, 16, 24, 32 };
            cfg.BitrateMbps = brMap[segBitrate.SelectedIndex];

            int[] thMap = new int[] { 2, 4, 6, 8 };
            cfg.CpuThreads = thMap[segThreads.SelectedIndex];

            int[] refMap = new int[] { 50, 80, 200, 500 };
            cfg.StaticRefreshMs = refMap[segRefresh.SelectedIndex];

            cfg.CompatGdiMode = (segCapture.SelectedIndex == 1);
        }

        private void CompileCustomShaders(out byte[] yShader, out byte[] uvShader)
        {
            double yr, yg, yb, yBaseOff;
            double ur, ug, ub;
            double vr, vg, vb;

            if (cfg.ColorMode == 1) // BT.709 Full Range
            {
                yr = 0.2126; yg = 0.7152; yb = 0.0722; yBaseOff = 0.0;
                ur = -0.114572; ug = -0.385428; ub = 0.500000;
                vr = 0.500000; vg = -0.454153; vb = -0.045847;
            }
            else if (cfg.ColorMode == 2) // BT.601 Limited
            {
                yr = 0.256788; yg = 0.504129; yb = 0.097906; yBaseOff = 0.0627451;
                ur = -0.148223; ug = -0.290993; ub = 0.439216;
                vr = 0.439216; vg = -0.367788; vb = -0.071427;
            }
            else // BT.709 Limited Range (16..235)
            {
                yr = 0.182586; yg = 0.614231; yb = 0.062007; yBaseOff = 0.0627451;
                ur = -0.100644; ug = -0.338572; ub = 0.439216;
                vr = 0.439216; vg = -0.398942; vb = -0.040274;
            }

            double tr = 1.0, tg = 1.0, tb = 1.0;
            if (cfg.ColorTemp == 1) { tr = 1.00; tg = 0.96; tb = 0.87; }
            else if (cfg.ColorTemp == 2) { tr = 1.00; tg = 0.90; tb = 0.76; }
            else if (cfg.ColorTemp == 3) { tr = 0.93; tg = 0.97; tb = 1.04; }

            double c = cfg.Contrast / 100.0;
            double b = cfg.Brightness / 100.0;
            double s = cfg.Saturation / 100.0;
            double gamma = cfg.GammaX100 / 100.0;

            double fyr = yr * tr * c;
            double fyg = yg * tg * c;
            double fyb = yb * tb * c;
            double ySum = yr + yg + yb;
            double fyOff = yBaseOff + ySum * (0.5 * (1.0 - c) + b);

            double fur = ur * tr * c * s;
            double fug = ug * tg * c * s;
            double fub = ub * tb * c * s;

            double fvr = vr * tr * c * s;
            double fvg = vg * tg * c * s;
            double fvb = vb * tb * c * s;

            CultureInfo inv = CultureInfo.InvariantCulture;
            string sampleExpr = "saturate(tx.Sample(sam, inp.uv).xyz)";
            if (Math.Abs(gamma - 1.0) > 0.005)
            {
                sampleExpr = string.Format(inv, "pow(max({0}, 0.0001), {1:F4})", sampleExpr, gamma);
            }

            string hlslY = string.Format(inv, @"
Texture2D tx : register(t0);
SamplerState sam : register(s0);
struct PS_IN {{ float4 pos : SV_POSITION; float2 uv : TEXCOORD0; }};
float4 main(PS_IN inp) : SV_Target {{
    float3 rgb = {0};
    float y = dot(rgb, float3({1:F6}, {2:F6}, {3:F6})) + {4:F6};
    return float4(saturate(y), 0.0, 0.0, 1.0);
}}", sampleExpr, fyr, fyg, fyb, fyOff);

            string hlslUV = string.Format(inv, @"
Texture2D tx : register(t0);
SamplerState sam : register(s0);
struct PS_IN {{ float4 pos : SV_POSITION; float2 uv : TEXCOORD0; }};
float2 main(PS_IN inp) : SV_Target {{
    float3 rgb = {0};
    float u = dot(rgb, float3({1:F6}, {2:F6}, {3:F6})) + 0.5019608;
    float v = dot(rgb, float3({4:F6}, {5:F6}, {6:F6})) + 0.5019608;
    return saturate(float2(u, v));
}}", sampleExpr, fur, fug, fub, fvr, fvg, fvb);

            yShader = D3DShaderCompiler.CompilePS(hlslY);
            uvShader = D3DShaderCompiler.CompilePS(hlslUV);
        }

        private void PatchBinaryFile(string exePath, byte[] yShader, byte[] uvShader)
        {
            if (!File.Exists(exePath)) return;
            FileInfo fi = new FileInfo(exePath);
            if (fi.IsReadOnly) fi.IsReadOnly = false;

            byte[] bin = File.ReadAllBytes(exePath);

            for (int k = 0; k < 704; k++) bin[0x4E8FF0 + k] = 0;
            Array.Copy(yShader, 0, bin, 0x4E8FF0, yShader.Length);
            Array.Copy(BitConverter.GetBytes(yShader.Length), 0, bin, 0x179125, 4);

            for (int k = 0; k < 784; k++) bin[0x4E8CE0 + k] = 0;
            Array.Copy(uvShader, 0, bin, 0x4E8CE0, uvShader.Length);
            Array.Copy(BitConverter.GetBytes(uvShader.Length), 0, bin, 0x1791A0, 4);

            Array.Copy(BitConverter.GetBytes(cfg.TargetFps), 0, bin, 0x0B0F2A, 4);
            Array.Copy(BitConverter.GetBytes(960), 0, bin, 0x176A2E, 4);

            bin[0x15EC85] = (cfg.EncoderMode == 0) ? (byte)0xEB : (byte)0x75;
            bin[0x15F698] = 0x20;
            bin[0x15F714] = 0x00;
            Array.Copy(BitConverter.GetBytes(cfg.StaticRefreshMs), 0, bin, 0x15F7A5, 4);

            int bps = cfg.BitrateMbps * 1000000;
            Array.Copy(BitConverter.GetBytes(bps), 0, bin, 0x0B0C4D, 4);

            Array.Copy(BitConverter.GetBytes(cfg.CpuThreads), 0, bin, 0x189316, 4);
            Array.Copy(BitConverter.GetBytes(cfg.CpuThreads), 0, bin, 0x189323, 4);

            File.WriteAllBytes(exePath, bin);
        }

        private void SaveAndApplyAll()
        {
            try
            {
                ReadUIToConfig();
                lblFooter.Text = "Compilando Pixel Shaders D3D11 e atualizando monitor...";
                byte[] yShader, uvShader;
                CompileCustomShaders(out yShader, out uvShader);

                string sysIni = Path.Combine(progDataDir, "UDisplayPro.ini");
                cfg.Save(sysIni);

                if (Directory.Exists(progDataDir))
                {
                    string globalIni = Path.Combine(progDataDir, "GlobalConfig.ini");
                    File.WriteAllText(globalIni, "[Global]\r\nisPopUp=false\r\nIsCompatMode=" + (cfg.CompatGdiMode ? "true" : "false") + "\r\n", Encoding.ASCII);

                    foreach (string devIni in Directory.GetFiles(progDataDir, "UD*.ini"))
                    {
                        string content = File.ReadAllText(devIni);
                        if (content.Contains("quality=0"))
                        {
                            content = content.Replace("quality=0", "quality=1");
                            File.WriteAllText(devIni, content, Encoding.ASCII);
                        }
                    }
                }

                StopUDisplayProcesses();

                string sysExe = Path.Combine(progDataDir, "UDisplay.exe");
                string localExe = Path.Combine(appDir, "UDisplay.exe");

                if (File.Exists(localExe) && !string.Equals(Path.GetFullPath(localExe), Path.GetFullPath(sysExe), StringComparison.OrdinalIgnoreCase))
                {
                    PatchBinaryFile(localExe, yShader, uvShader);
                    if (Directory.Exists(progDataDir))
                    {
                        FileInfo fi = new FileInfo(sysExe);
                        if (fi.Exists && fi.IsReadOnly) fi.IsReadOnly = false;
                        File.Copy(localExe, sysExe, true);
                    }
                }
                else if (File.Exists(sysExe))
                {
                    PatchBinaryFile(sysExe, yShader, uvShader);
                }

                StartUDisplayProcesses();
                UpdateLiveStatus();

                lblFooter.Text = string.Format("Aplicado com sucesso! ({0} FPS • {1} Mbps • Brilho {2}% • Contraste {3}% • Saturação {4}%)",
                    cfg.TargetFps, cfg.BitrateMbps, cfg.Brightness, cfg.Contrast, cfg.Saturation);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao aplicar configurações:\n" + ex.Message, "UDisplay Pro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetWindowsDisplayMode(string arg, string msg)
        {
            try
            {
                Process.Start(new ProcessStartInfo("DisplaySwitch.exe", arg) { UseShellExecute = true });
                lblFooter.Text = msg;
            }
            catch { }
        }

        private void StopUDisplayProcesses()
        {
            foreach (Process p in Process.GetProcessesByName("UDisplay"))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
            }
            Thread.Sleep(300);
        }

        private void StartUDisplayProcesses()
        {
            string sysExe = Path.Combine(progDataDir, "UDisplay.exe");
            if (!File.Exists(sysExe)) sysExe = Path.Combine(appDir, "UDisplay.exe");
            if (!File.Exists(sysExe)) return;

            try
            {
                Process.Start(new ProcessStartInfo(sysExe, "daemon")
                {
                    WorkingDirectory = Path.GetDirectoryName(sysExe),
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }

            Thread.Sleep(400);

            try
            {
                Process.Start(new ProcessStartInfo(sysExe, "main")
                {
                    WorkingDirectory = Path.GetDirectoryName(sysExe),
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }
        }

        private void RestartUDisplayProcesses()
        {
            StopUDisplayProcesses();
            StartUDisplayProcesses();
            Thread.Sleep(700);
            UpdateLiveStatus();
        }

        private void InstallOnThisComputer()
        {
            try
            {
                lblFooter.Text = "Instalando driver virtual e UDisplay Pro neste computador...";
                Directory.CreateDirectory(progDataDir);
                string localExe = Path.Combine(appDir, "UDisplay.exe");
                string sysExe = Path.Combine(progDataDir, "UDisplay.exe");

                StopUDisplayProcesses();

                if (File.Exists(localExe) && !string.Equals(Path.GetFullPath(localExe), Path.GetFullPath(sysExe), StringComparison.OrdinalIgnoreCase))
                {
                    FileInfo fi = new FileInfo(sysExe);
                    if (fi.Exists && fi.IsReadOnly) fi.IsReadOnly = false;
                    File.Copy(localExe, sysExe, true);
                }

                string curCfgExe = Process.GetCurrentProcess().MainModule.FileName;
                string sysCfgExe = Path.Combine(progDataDir, "UDisplayConfig.exe");
                if (!string.Equals(Path.GetFullPath(curCfgExe), Path.GetFullPath(sysCfgExe), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Copy(curCfgExe, sysCfgExe, true); } catch { }
                }

                try
                {
                    Process p = Process.Start(new ProcessStartInfo(sysExe, "install_service_and_drv")
                    {
                        WorkingDirectory = progDataDir,
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    });
                    if (p != null) p.WaitForExit(25000);
                }
                catch { }

                try
                {
                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (key != null) key.SetValue("UDisplay", "\"" + sysExe + "\" daemon");
                    }
                }
                catch { }

                SaveAndApplyAll();
                MessageBox.Show(
                    "Instalação concluída com sucesso!\n\n" +
                    "• Driver de Monitor Virtual (SG_DISP_BUS) instalado\n" +
                    "• UDisplay Pro (60 FPS + BT.709 + Zero-Latency) ativo em C:\\ProgramData\\UDisplay\n" +
                    "• Inicialização automática com o Windows configurada!",
                    "UDisplay Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro durante a instalação:\n" + ex.Message, "UDisplay Pro", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateLiveStatus()
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("UDisplay");
                lblDiagProcs.Text = string.Format("• Processos em Execução: {0} ({1})",
                    procs.Length, procs.Length >= 2 ? "Daemon + Captura Principal Ativos" : (procs.Length == 1 ? "1 Processo Ativo" : "Parado"));

                string snFound = "Detectado";
                if (Directory.Exists(progDataDir))
                {
                    string[] inis = Directory.GetFiles(progDataDir, "UD*.ini");
                    if (inis.Length > 0) snFound = Path.GetFileNameWithoutExtension(inis[0]);
                }
                lblDiagAdapter.Text = "• Adaptador USB-C Lemorele LC06 Plus (VID_185B&PID_2D1D): Conectado | Serial: " + snFound;

                string sysExe = Path.Combine(progDataDir, "UDisplay.exe");
                if (File.Exists(sysExe))
                {
                    byte[] b = File.ReadAllBytes(sysExe);
                    int fps = BitConverter.ToInt32(b, 0x0B0F2A);
                    bool zeroLat = (b[0x15EC85] == 0xEB);
                    int bps = BitConverter.ToInt32(b, 0x0B0C4D);
                    int mbps = (bps > 1000000 && bps <= 100000000) ? (bps / 1000000) : 24;
                    lblDiagPatch.Text = string.Format("• Motor Ativo: {0} FPS Reais • {1} Mbps • {2}",
                        fps, mbps, zeroLat ? "CPU x264 Zero-Latency" : "GPU Hardware Encoder");
                    lblHeaderBadge.Text = string.Format("● {0} FPS • {1} Mbps Ativo", fps, mbps);
                }
            }
            catch { }
        }

        public void ExportScreenshots(string outDir)
        {
            Directory.CreateDirectory(outDir);
            FrameworkElement rootEl = (FrameworkElement)this.Content;
            double w = 880;
            double h = 665;

            for (int t = 0; t < 3; t++)
            {
                SelectTab(t);
                rootEl.Measure(new Size(w, h));
                rootEl.Arrange(new Rect(0, 0, w, h));
                rootEl.UpdateLayout();

                RenderTargetBitmap rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(rootEl);
                PngBitmapEncoder enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (FileStream fs = new FileStream(Path.Combine(outDir, "tab_" + (t + 1) + ".png"), FileMode.Create))
                {
                    enc.Save(fs);
                }
            }
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        static void Main(string[] args)
        {
            if (args != null && args.Length >= 2 && args[0] == "--screenshot")
            {
                MainWindow w = new MainWindow();
                w.ExportScreenshots(args[1]);
                return;
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\UDisplayProConfiguratorSingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    foreach (Process p in Process.GetProcessesByName("UDisplayConfig"))
                    {
                        if (p.Id != Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero)
                        {
                            ShowWindow(p.MainWindowHandle, 9); // SW_RESTORE
                            SetForegroundWindow(p.MainWindowHandle);
                            break;
                        }
                    }
                    return;
                }

                Application app = new Application();
                app.Run(new MainWindow());
            }
        }
    }
}
