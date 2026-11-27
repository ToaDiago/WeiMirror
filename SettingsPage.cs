using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MirrorDesk
{
    internal sealed class SettingsPage : Panel
    {
        readonly GlassGrid frame, navigation;
        readonly Button[] sections;
        readonly Control[] pages;
        AppPalette palette=new AppPalette(0,false);
        int selection;
        bool railLayout=true;
        float scale=1f;

        public SettingsPage(Control casting,Control appearance,Control components,Control tools)
        {
            Name="SettingsPage";
            AccessibleName="设置";
            Dock=DockStyle.Fill;
            Margin=Padding.Empty;
            DoubleBuffered=true;
            SetStyle(ControlStyles.ResizeRedraw,true);

            frame=GlassUi.Grid(2,2);
            frame.Padding=new Padding(21,3,21,11);
            Theme.Cols(frame,132,-1); Theme.Rows(frame,0,-1);
            navigation=GlassUi.Grid(4,4);
            navigation.Name="SettingsNavigation";
            navigation.Dock=DockStyle.Top;
            navigation.Height=180;
            navigation.Margin=new Padding(0,8,12,0);
            for (int i=0;i<4;i++) navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,i==0 ? 100 : 0));
            Theme.Rows(navigation,45,45,45,45);
            sections=new Button[] {
                new GlassButton("投屏") { SegmentChoice=true,Symbol=InterfaceGlyph.Casting },
                new GlassButton("外观") { SegmentChoice=true,Symbol=InterfaceGlyph.Appearance },
                new GlassButton("组件") { SegmentChoice=true,Symbol=InterfaceGlyph.Components },
                new GlassButton("工具与关于") { SegmentChoice=true,Symbol=InterfaceGlyph.Tools,AccessibleName="工具与关于" }
            };
            pages=new[] { casting,appearance,components,tools };
            var body=new ConsoleSurface(1,1) { Padding=new Padding(10,9,10,12), Margin=new Padding(0,7,0,0) };
            Theme.Cols(body,-1); Theme.Rows(body,-1);
            var deck=new GlassPanel { Dock=DockStyle.Fill, Margin=Padding.Empty };
            body.Controls.Add(deck,0,0);
            for (int i=0;i<sections.Length;i++) {
                int index=i;
                var button=sections[i];
                button.Name="SettingsSection"+i;
                button.Font=Theme.Font(10.5f,true);
                button.TextAlign=ContentAlignment.MiddleLeft;
                button.Click+=delegate { SelectSection(index); };
                navigation.Controls.Add(button,0,i);
                deck.Controls.Add(pages[i]);
            }
            frame.Controls.Add(navigation,0,1);
            frame.Controls.Add(body,1,1);
            Controls.Add(frame);
            SelectSection(0);
        }

        internal void SelectSection(int index)
        {
            selection=index;
            for (int i=0;i<pages.Length;i++) {
                pages[i].Visible=i==selection;
                palette.StyleButton(sections[i],i==selection);
                sections[i].AccessibleDescription=i==selection ? "当前分类" : null;
            }
        }

        internal void ApplyPalette(AppPalette next)
        {
            palette=next;
            SelectSection(selection);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { GlassUi.ControlBackground(e.Graphics,this); }

        internal void ScaleNavigation(float uiScale)
        {
            scale=uiScale;
            ArrangeNavigation();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ArrangeNavigation();
        }

        int UiPixels(int value) { return DpiLayout.Pixels(value,scale); }

        void ArrangeNavigation()
        {
            if (navigation==null) return;
            bool rail=ClientSize.Width>=UiPixels(536);
            frame.SuspendLayout();
            navigation.SuspendLayout();
            try {
                if (rail!=railLayout) {
                    if (rail) {
                        frame.SetColumnSpan(navigation,1);
                        frame.SetCellPosition(navigation,new TableLayoutPanelCellPosition(0,1));
                    } else {
                        frame.SetCellPosition(navigation,new TableLayoutPanelCellPosition(0,0));
                        frame.SetColumnSpan(navigation,2);
                    }
                    for (int i=0;i<sections.Length;i++) {
                        navigation.SetCellPosition(sections[i],new TableLayoutPanelCellPosition(rail ? 0 : i,rail ? i : 0));
                        sections[i].TextAlign=rail ? ContentAlignment.MiddleLeft : ContentAlignment.MiddleCenter;
                    }
                    sections[3].Text=rail ? "工具与关于" : "工具";
                    railLayout=rail;
                }
                frame.ColumnStyles[0].Width=rail ? UiPixels(132) : 0;
                frame.RowStyles[0].Height=rail ? 0 : UiPixels(52);
                navigation.Segmented=!rail;
                navigation.Height=UiPixels(rail ? 180 : 52);
                navigation.Margin=rail ? new Padding(0,UiPixels(8),UiPixels(12),0) : Padding.Empty;
                navigation.Padding=rail ? Padding.Empty : new Padding(UiPixels(4));
                for (int i=0;i<sections.Length;i++) {
                    navigation.ColumnStyles[i].Width=rail ? (i==0 ? 100 : 0) : 1;
                    navigation.RowStyles[i].Height=rail ? UiPixels(45) : i==0 ? UiPixels(44) : 0;
                }
            } finally {
                navigation.ResumeLayout(true);
                frame.ResumeLayout(true);
            }
            navigation.Invalidate(true);
        }

    }

    internal sealed partial class MainForm
    {
        readonly ComboBox appearance = Picker(), interfaceFont = Picker(),
            textPercent = Picker(), interfaceSize = Picker();
        readonly CheckBox darkMode = new GlassToggle("深色模式");
        readonly CheckBox showToolbar = new GlassToggle("始终显示投屏工具栏");
        SettingsPage settingsPage;

        string InterfaceFont { get { return interfaceFont.SelectedIndex>0 ? (string)interfaceFont.SelectedItem : ""; } }
        int TextPercent { get { return 90+textPercent.SelectedIndex*10; } }
        float TextScale { get { return TextPercent/100f; } }
        float InterfaceScale { get { return .90f+interfaceSize.SelectedIndex*.10f; } }

        void ChangeInterface(bool resize)
        {
            if (IsHandleCreated) {
                ScaleConsole(consoleDpiValue);
                if (resize) FitConsole();
            }
        }

        protected override bool ProcessCmdKey(ref Message message,Keys keyData)
        {
            if (keyData==(Keys.Control|Keys.Oemcomma) && settingsButton.Enabled && !setupPage.Visible) {
                ShowConsolePage(settingsPage);
                return true;
            }
            if (keyData==Keys.Escape && settingsButton.Enabled && !viewport.Visible &&
                !(ActiveControl is ComboBox picker && picker.DroppedDown)) {
                ReturnToConsole();
                return true;
            }
            return base.ProcessCmdKey(ref message,keyData);
        }

        static GlassGrid SettingsGrid(params int[] rows)
        {
            var grid=GlassUi.Grid(2,rows.Length);
            grid.RowRules=true;
            grid.Dock=DockStyle.Top;
            grid.Padding=new Padding(10,3,11,4);
            grid.Height=rows.Sum()+grid.Padding.Vertical;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,34));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,66));
            Theme.Rows(grid,rows);
            return grid;
        }

        static Panel ScrollSettings(Control content)
        {
            var page=new GlassPanel { Dock=DockStyle.Fill, AutoScroll=true };
            page.Controls.Add(content);
            return page;
        }

        static void AddSetting(TableLayoutPanel grid,int row,string label,Control field)
        {
            var caption=GlassUi.Label(label,10.5f,Theme.Ink);
            caption.Margin=new Padding(0,1,6,1);
            grid.Controls.Add(caption,0,row);
            field.AccessibleName=label;
            field.Font=Theme.Font(11);
            field.Dock=DockStyle.None;
            field.Anchor=AnchorStyles.Left | AnchorStyles.Right;
            grid.Controls.Add(field,1,row);
        }

        static void FullSetting(TableLayoutPanel grid,int row,Control field)
        {
            grid.Controls.Add(field,0,row);
            grid.SetColumnSpan(field,2);
        }

        void BuildSettings()
        {
            var casting=SettingsGrid(45,45,43,43);
            quality.Items.AddRange(WirelessProfile.Choices);
            quality.SelectedIndex=WirelessProfile.DefaultIndex;
            quality.Name="AirPlayQuality";
            name.BorderStyle=BorderStyle.FixedSingle;
            AddSetting(casting,0,"接收端名称",name);
            AddSetting(casting,1,"无线画质",quality);
            audio.Font=showToolbar.Font=Theme.Font(11);
            FullSetting(casting,2,audio);
            FullSetting(casting,3,showToolbar);

            var looks=SettingsGrid(43,43,43,43,42);
            appearance.Items.AddRange(AppPalette.Names);
            appearance.SelectedIndex=0;
            appearance.Name="AppearanceTheme";
            interfaceFont.Items.Add("默认（微软雅黑）");
            var families=new List<string>();
            foreach (var family in FontFamily.Families)
                using (family) families.Add(family.Name);
            interfaceFont.Items.AddRange(families.OrderBy(family=>family,StringComparer.CurrentCultureIgnoreCase).ToArray());
            interfaceFont.SelectedIndex=0;
            interfaceFont.Name="InterfaceFont";
            textPercent.Items.AddRange(new object[] { "90%","100%","110%","120%" });
            textPercent.SelectedIndex=1;
            textPercent.Name="TextPercent";
            interfaceSize.Items.AddRange(new object[] { "小","中","大" });
            interfaceSize.SelectedIndex=UserPreferences.DefaultInterfaceSize;
            interfaceSize.Name="InterfaceSize";
            AddSetting(looks,0,"主题",appearance);
            AddSetting(looks,1,"界面字体",interfaceFont);
            AddSetting(looks,2,"字号",textPercent);
            AddSetting(looks,3,"界面大小",interfaceSize);
            darkMode.Font=Theme.Font(11);
            FullSetting(looks,4,darkMode);

            var components=SettingsGrid(32,55,43,43);
            components.RowRules=false;
            FullSetting(components,0,pathCaption);
            componentPath.AccessibleName="投屏组件目录";
            componentPath.Font=Theme.Font(10.5f);
            componentPath.Dock=DockStyle.Fill;
            componentPath.ScrollBars=ScrollBars.Vertical;
            componentPath.Margin=new Padding(0,0,0,7);
            componentPath.BorderStyle=BorderStyle.FixedSingle;
            FullSetting(components,1,componentPath);
            var actions=new[] { openPath,driver };
            for (int i=0;i<actions.Length;i++) {
                actions[i].Font=Theme.Font(10.5f);
                actions[i].Margin=new Padding(0,0,0,5);
                FullSetting(components,i+2,actions[i]);
            }
            settingsPage=new SettingsPage(ScrollSettings(casting),ScrollSettings(looks),ScrollSettings(components),BuildToolsPage());
        }
    }
}
