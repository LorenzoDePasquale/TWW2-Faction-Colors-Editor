using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Common;
using Filetypes;
using GongSolutions.Wpf.DragDrop;
using Microsoft.Win32;

namespace TWW2_Faction_Colors_Editor
{
    public partial class MainWindow : Window, INotifyPropertyChanged, IDropTarget
    {
        PackFile packFile;
        PackFileCodec codec = new PackFileCodec();
        DBFile bannerTables, uniformsTables, vanillaBannersTables, vanillaUniformsTables, currentBannerTables, currentUniformsTables;
        Dictionary<string, string> factionNames;
        ObservableCollection<FactionItem> factions;
        CollectionViewSource factionsCollection;
        string filterText, gamePath;
        bool colorChanged = true;
        FactionItem selectedFaction;
        Stack<(byte[], string)> undoStack = new Stack<(byte[], string)>();
        Stack<(byte[], string)> redoStack = new Stack<(byte[], string)>();

        public static RoutedCommand UndoCommand, RedoCommand;
        public event PropertyChangedEventHandler PropertyChanged;
        public ICollectionView SourceCollection
        {
            get
            {
                return factionsCollection.View;
            }
        }
        public ImageSource SelectedFactionImage
        {
            get
            {
                return selectedFaction.ImageSource;
            }
        }
        public string SelectedFactionName
        {
            get
            {
                return selectedFaction.Name;
            }
        }
        public string FilterText
        {
            get
            {
                return filterText;
            }
            set
            {
                filterText = value;
                factionsCollection.View.Refresh();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("FilterText"));
            }
        }


        public MainWindow()
        {
            UndoCommand = new RoutedCommand();
            UndoCommand.InputGestures.Add(new KeyGesture(Key.Z, ModifierKeys.Control));
            RedoCommand = new RoutedCommand();
            RedoCommand.InputGestures.Add(new KeyGesture(Key.Y, ModifierKeys.Control));

            InitializeComponent();
            bannerGroupBox.Visibility = Visibility.Hidden;
            uniformGroupBox.Visibility = Visibility.Hidden;
            buttonUndo.Visibility = Visibility.Hidden;
            buttonRedo.Visibility = Visibility.Hidden;

            factions = new ObservableCollection<FactionItem>();
            factions.CollectionChanged += (sender, e) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SourceCollection"));
            factionsCollection = new CollectionViewSource();
            factionsCollection.Source = factions;
            factionsCollection.Filter += (object sender, FilterEventArgs e) =>
            {
                if (string.IsNullOrEmpty(FilterText))
                {
                    e.Accepted = true;
                    return;
                }
                FactionItem f = e.Item as FactionItem;
                if (FilterText == "*")
                    e.Accepted = f.Modified;
                else if (FilterText[0] == '-')
                    e.Accepted = bannerTables.Entries[f.Index][0].Value.Contains($"_{FilterText.Substring(1)}_");
                else
                    e.Accepted = f.Name.ToUpper().Contains(FilterText.ToUpper());
            };

            gamePath = GetGamePath();
            if (gamePath is null)
            {
                MessageBox.Show("Total War Warhammer II is not installed or could not be found", "Error");
                Close();
            }

            RegistryKey regKey = Registry.CurrentUser.OpenSubKey(@"Software\The Creative Assembly\Launcher\594570\mods");
            if (regKey.GetValue("faction_colors.pack") != null && File.Exists(gamePath + "\\data\\faction_colors.pack"))
            {
                checkBoxEnable.Checked -= checkBoxEnable_CheckedChanged;
                checkBoxEnable.IsChecked = true;
                checkBoxEnable.Checked += checkBoxEnable_CheckedChanged;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            DBTypeMap.Instance.initializeFromFile("schema.xml");

            packFile = codec.Open("vanilla.pack");
            vanillaBannersTables = DBDecoder.Decode("faction_banners_tables", packFile.Files[0].Data);
            vanillaUniformsTables = DBDecoder.Decode("faction_uniform_colours_tables", packFile.Files[1].Data);
            packFile = codec.Open("faction_colors.pack");
            bannerTables = DBDecoder.Decode("faction_banners_tables", packFile.Files[0].Data);
            uniformsTables = DBDecoder.Decode("faction_uniform_colours_tables", packFile.Files[1].Data);
            currentBannerTables = DBDecoder.Decode("faction_banners_tables", packFile.Files[0].Data);
            currentUniformsTables = DBDecoder.Decode("faction_uniform_colours_tables", packFile.Files[1].Data);

            factionNames = new Dictionary<string, string>();
            foreach (var line in File.ReadAllLines("source.txt"))
            {
                int indexOfSpace = line.IndexOf(' ');
                factionNames.Add(line.Substring(0, indexOfSpace), line.Substring(indexOfSpace));
            }

            List<FactionItem> temp = new List<FactionItem>();
            for (int i = 0; i < factionNames.Count; i++)
            {
                string imagePath = $"flags/{bannerTables.Entries[i][0].Value}/mon_64.png";
                if (File.Exists(imagePath))
                {
                    string factionName = factionNames[bannerTables.Entries[i][0].Value];
                    bool modified = !vanillaBannersTables.Entries[i].SequenceEqual(bannerTables.Entries[i]) && !vanillaUniformsTables.Entries.SequenceEqual(uniformsTables.Entries);
                    temp.Add(new FactionItem(imagePath, factionName, i, modified));
                }
            }
            temp.Sort((x, y) => String.Compare(x.Name, y.Name));
            temp.ForEach(x => factions.Add(x));
        }

        private void buttonSave_Click(object sender, RoutedEventArgs e)
        {
            packFile.Files[0].Data = DBDecoder.Encode("faction_banners_tables", bannerTables);
            packFile.Files[1].Data = DBDecoder.Encode("faction_uniform_colours_tables", uniformsTables);
            codec.Save(packFile);

            currentBannerTables = DBDecoder.Decode("faction_banners_tables", packFile.Files[0].Data);
            currentUniformsTables = DBDecoder.Decode("faction_uniform_colours_tables", packFile.Files[1].Data);

            if (checkBoxEnable.IsChecked == true)
                CreateModdedPackFile();

            Storyboard storyboardIn = FindResource("SavedAnimationIn") as Storyboard;
            Storyboard.SetTarget(storyboardIn, labelSaved);
            storyboardIn.Begin();
            Storyboard storyboardOut = FindResource("SavedAnimationOut") as Storyboard;
            Storyboard.SetTarget(storyboardOut, labelTitle);
            storyboardOut.Begin();

            Task.Delay(2000).ContinueWith((t) =>
            {
                App.Current.Dispatcher.Invoke((Action)delegate
                {
                    Storyboard storyboardInRev = FindResource("SavedAnimationInRev") as Storyboard;
                    Storyboard.SetTarget(storyboardInRev, labelSaved);
                    storyboardInRev.Begin();
                    Storyboard storyboardOutRev = FindResource("SavedAnimationOutRev") as Storyboard;
                    Storyboard.SetTarget(storyboardOutRev, labelTitle);
                    storyboardOutRev.Begin();
                });
            });
        }

        private void factionsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            colorChanged = false;
            FactionItem item = (FactionItem)factionsListView.SelectedItem;

            DBRow row = bannerTables.Entries[item.Index];
            byte[] colorValues = row.Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
            bannerPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[2], colorValues[1], colorValues[0]);
            bannerSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[5], colorValues[4], colorValues[3]);
            bannerTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[8], colorValues[7], colorValues[6]);

            row = uniformsTables.Entries[item.Index];
            colorValues = row.Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
            uniformsPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[0], colorValues[1], colorValues[2]);
            uniformsSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[3], colorValues[4], colorValues[5]);
            uniformsTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[6], colorValues[7], colorValues[8]);

            colorChanged = true;
            selectedFaction = item;

            bannerGroupBox.Visibility = Visibility.Visible;
            uniformGroupBox.Visibility = Visibility.Visible;
            buttonUndo.Visibility = Visibility.Visible;
            buttonRedo.Visibility = Visibility.Visible;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SelectedFactionImage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SelectedFactionName"));

            undoStack.Clear();
            redoStack.Clear();
        }

        private void buttonVanillaBanner_Click(object sender, RoutedEventArgs e)
        {
            if (factionsListView.SelectedIndex == -1) return;

            FactionItem item = (FactionItem)factionsListView.SelectedItem;
            DBRow row = vanillaBannersTables.Entries[item.Index];
            byte[] colorValues = row.Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
            bannerPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[2], colorValues[1], colorValues[0]);
            bannerSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[5], colorValues[4], colorValues[3]);
            bannerTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[8], colorValues[7], colorValues[6]);

            ColorsToBannerTables(selectedFaction.Index, bannerPrimaryColorPicker.SelectedColor.Value, bannerSecondaryColorPicker.SelectedColor.Value, bannerTertiaryColorPicker.SelectedColor.Value);
        }

        private void buttonVanillaUniforms_Click(object sender, RoutedEventArgs e)
        {
            if (factionsListView.SelectedIndex == -1) return;

            FactionItem item = (FactionItem)factionsListView.SelectedItem;
            DBRow row = vanillaUniformsTables.Entries[item.Index];
            byte[] colorValues = row.Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
            uniformsPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[0], colorValues[1], colorValues[2]);
            uniformsSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[3], colorValues[4], colorValues[5]);
            uniformsTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[6], colorValues[7], colorValues[8]);

            ColorsToUniformsTables(selectedFaction.Index, uniformsPrimaryColorPicker.SelectedColor.Value, uniformsSecondaryColorPicker.SelectedColor.Value, uniformsTertiaryColorPicker.SelectedColor.Value);
        }

        private void bannersColorPicker_changedColor(object sender, RoutedEventArgs e)
        {
            if (!colorChanged) return;

            int index = selectedFaction.Index;
            byte[] values = bannerTables.Entries[index].Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
            undoStack.Push((values, "banner"));
            redoStack.Clear();

            ColorsToBannerTables(index, bannerPrimaryColorPicker.SelectedColor.Value, bannerSecondaryColorPicker.SelectedColor.Value, bannerTertiaryColorPicker.SelectedColor.Value);
        }

        private void uniformsColorPicker_changedColor(object sender, RoutedEventArgs e)
        {
            if (!colorChanged) return;

            int index = selectedFaction.Index;
            byte[] values = uniformsTables.Entries[index].Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
            undoStack.Push((values, "uniforms"));
            redoStack.Clear();

            ColorsToUniformsTables(index, uniformsPrimaryColorPicker.SelectedColor.Value, uniformsSecondaryColorPicker.SelectedColor.Value, uniformsTertiaryColorPicker.SelectedColor.Value);
        }

        private void buttonClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void checkBoxEnable_CheckedChanged(object sender, RoutedEventArgs e)
        {
            //TODO: check for UAC
            if (checkBoxEnable.IsChecked == true)
            {
                CreateModdedPackFile();

                RegistryKey regKey = Registry.CurrentUser.OpenSubKey(@"Software\The Creative Assembly\Launcher\594570\mods", true);
                regKey.SetValue("faction_colors.pack", "0|0|1|65540", RegistryValueKind.String);

            }
            else
            {
                File.Delete(gamePath + "\\data\\faction_colors.pack");
                RegistryKey regKey = Registry.CurrentUser.OpenSubKey(@"Software\The Creative Assembly\Launcher\594570\mods", true);
                regKey.DeleteValue("faction_colors.pack");
            }
        }

        private void buttonUndoBanner_Click(object sender, RoutedEventArgs e)
        {
            colorChanged = false;
            FactionItem item = (FactionItem)factionsListView.SelectedItem;

            DBRow row = currentBannerTables.Entries[item.Index];
            byte[] colorValues = row.Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
            bannerPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[2], colorValues[1], colorValues[0]);
            bannerSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[5], colorValues[4], colorValues[3]);
            bannerTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[8], colorValues[7], colorValues[6]);

            colorChanged = true;
            ColorsToBannerTables(selectedFaction.Index, bannerPrimaryColorPicker.SelectedColor.Value, bannerSecondaryColorPicker.SelectedColor.Value, bannerTertiaryColorPicker.SelectedColor.Value);

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SelectedFactionImage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SelectedFactionName"));

            undoStack.Clear();
            redoStack.Clear();
        }

        private void buttonUndoUniforms_Click(object sender, RoutedEventArgs e)
        {
            colorChanged = false;
            FactionItem item = (FactionItem)factionsListView.SelectedItem;

            DBRow row = currentUniformsTables.Entries[item.Index];
            byte[] colorValues = row.Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
            uniformsPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[0], colorValues[1], colorValues[2]);
            uniformsSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[3], colorValues[4], colorValues[5]);
            uniformsTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[6], colorValues[7], colorValues[8]);

            ColorsToUniformsTables(selectedFaction.Index, uniformsPrimaryColorPicker.SelectedColor.Value, uniformsSecondaryColorPicker.SelectedColor.Value, uniformsTertiaryColorPicker.SelectedColor.Value);
            colorChanged = true;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SelectedFactionImage"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SelectedFactionName"));

            undoStack.Clear();
            redoStack.Clear();
        }

        private void Undo(object sender, ExecutedRoutedEventArgs e)
        {
            if (undoStack.Count < 1) return;

            byte[] colorValues;
            var action = undoStack.Pop();
            colorChanged = false;
            if (action.Item2 == "banner")
            {
                byte[] values = bannerTables.Entries[selectedFaction.Index].Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
                redoStack.Push((values, "banner"));

                colorValues = action.Item1;
                bannerPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[2], colorValues[1], colorValues[0]);
                bannerSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[5], colorValues[4], colorValues[3]);
                bannerTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[8], colorValues[7], colorValues[6]);

                ColorsToBannerTables(selectedFaction.Index, bannerPrimaryColorPicker.SelectedColor.Value, bannerSecondaryColorPicker.SelectedColor.Value, bannerTertiaryColorPicker.SelectedColor.Value);
            }
            else
            {
                byte[] values = uniformsTables.Entries[selectedFaction.Index].Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
                redoStack.Push((values, "uniforms"));

                colorValues = action.Item1;
                uniformsPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[0], colorValues[1], colorValues[2]);
                uniformsSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[3], colorValues[4], colorValues[5]);
                uniformsTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[6], colorValues[7], colorValues[8]);

                ColorsToUniformsTables(selectedFaction.Index, uniformsPrimaryColorPicker.SelectedColor.Value, uniformsSecondaryColorPicker.SelectedColor.Value, uniformsTertiaryColorPicker.SelectedColor.Value);
            }
            colorChanged = true;
        }

        private void Redo(object sender, ExecutedRoutedEventArgs e)
        {
            if (redoStack.Count < 1) return;

            byte[] colorValues;
            var action = redoStack.Pop();
            colorChanged = false;
            if (action.Item2 == "banner")
            {
                byte[] values = bannerTables.Entries[selectedFaction.Index].Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
                undoStack.Push((values, "banner"));

                colorValues = action.Item1;
                bannerPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[2], colorValues[1], colorValues[0]);
                bannerSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[5], colorValues[4], colorValues[3]);
                bannerTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[8], colorValues[7], colorValues[6]);

                ColorsToBannerTables(selectedFaction.Index, bannerPrimaryColorPicker.SelectedColor.Value, bannerSecondaryColorPicker.SelectedColor.Value, bannerTertiaryColorPicker.SelectedColor.Value);
            }
            else
            {
                byte[] values = uniformsTables.Entries[selectedFaction.Index].Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
                undoStack.Push((values, "uniforms"));

                colorValues = action.Item1;
                uniformsPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[0], colorValues[1], colorValues[2]);
                uniformsSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[3], colorValues[4], colorValues[5]);
                uniformsTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[6], colorValues[7], colorValues[8]);

                ColorsToUniformsTables(selectedFaction.Index, uniformsPrimaryColorPicker.SelectedColor.Value, uniformsSecondaryColorPicker.SelectedColor.Value, uniformsTertiaryColorPicker.SelectedColor.Value);
            }
            colorChanged = true;
        }

        private void buttonMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void buttonClear_Click(object sender, RoutedEventArgs e)
        {
            FilterText = "";
        }

        private void buttonLaunchGame_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("steam://rungameid/594570");
            Close();
        }

        #region "Drag drop"

        private void factionsListView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DependencyObject obj = factionsListView.ContainerFromElement((Visual)e.OriginalSource);
                if (obj != null)
                {
                    FrameworkElement element = obj as FrameworkElement;
                    if (element != null)
                    {
                        FactionItem item = (element as ListViewItem).Content as FactionItem;
                        
                        if (item != null && factionsListView.Items.Contains(item))
                        {
                            factionsListView.SelectedItem = item;
                        }
                    }
                }
            }
        }

        private void factionsListView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.GetPosition(factionsListView).X > 200) e.Handled = false;
            else e.Handled = true;
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        void IDropTarget.DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.VisualTarget is GroupBox)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
                dropInfo.Effects = DragDropEffects.Copy;
            }
        }

        void IDropTarget.Drop(IDropInfo dropInfo)
        {
            FactionItem item = (FactionItem)dropInfo.Data;
            colorChanged = false;
            if (((GroupBox)dropInfo.VisualTarget).Header.ToString() == "Banner" )
            {
                DBRow row = vanillaBannersTables.Entries[item.Index];
                byte[] colorValues = row.Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
                bannerPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[2], colorValues[1], colorValues[0]);
                bannerSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[5], colorValues[4], colorValues[3]);
                bannerTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[8], colorValues[7], colorValues[6]);

                int index = selectedFaction.Index;
                byte[] values = bannerTables.Entries[index].Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
                undoStack.Push((values, "banner"));
                redoStack.Clear();

                ColorsToBannerTables(index, bannerPrimaryColorPicker.SelectedColor.Value, bannerSecondaryColorPicker.SelectedColor.Value, bannerTertiaryColorPicker.SelectedColor.Value);
            }
            else if (((GroupBox)dropInfo.VisualTarget).Header.ToString() == "Uniforms")
            {
                DBRow row = vanillaUniformsTables.Entries[item.Index];
                byte[] colorValues = row.Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
                uniformsPrimaryColorPicker.SelectedColor = Color.FromRgb(colorValues[0], colorValues[1], colorValues[2]);
                uniformsSecondaryColorPicker.SelectedColor = Color.FromRgb(colorValues[3], colorValues[4], colorValues[5]);
                uniformsTertiaryColorPicker.SelectedColor = Color.FromRgb(colorValues[6], colorValues[7], colorValues[8]);

                int index = selectedFaction.Index;
                byte[] values = uniformsTables.Entries[index].Where(x => x.Info.TypeCode == TypeCode.Int32).Select(x => Byte.Parse(x.Value)).ToArray();
                undoStack.Push((values, "uniforms"));
                redoStack.Clear();

                ColorsToUniformsTables(index, uniformsPrimaryColorPicker.SelectedColor.Value, uniformsSecondaryColorPicker.SelectedColor.Value, uniformsTertiaryColorPicker.SelectedColor.Value);
            }
            colorChanged = true;
        }

        #endregion

        #region "Utilities"

        private void ColorsToBannerTables(int index, params Color[] colors)
        {
            bannerTables.Entries[index][1].Value = colors[0].B.ToString();
            bannerTables.Entries[index][2].Value = colors[0].G.ToString();
            bannerTables.Entries[index][3].Value = colors[0].R.ToString();
            bannerTables.Entries[index][4].Value = colors[1].B.ToString();
            bannerTables.Entries[index][5].Value = colors[1].G.ToString();
            bannerTables.Entries[index][6].Value = colors[1].R.ToString();
            bannerTables.Entries[index][8].Value = colors[2].B.ToString();
            bannerTables.Entries[index][9].Value = colors[2].G.ToString();
            bannerTables.Entries[index][10].Value = colors[2].R.ToString();

            factions[factions.IndexOf(selectedFaction)].Modified = !vanillaBannersTables.Entries[index].SequenceEqual(bannerTables.Entries[index]) || !vanillaUniformsTables.Entries[index].SequenceEqual(uniformsTables.Entries[index]);
        }

        private void ColorsToUniformsTables(int index, params Color[] colors)
        {
            uniformsTables.Entries[index][1].Value = colors[0].R.ToString();
            uniformsTables.Entries[index][2].Value = colors[0].G.ToString();
            uniformsTables.Entries[index][3].Value = colors[0].B.ToString();
            uniformsTables.Entries[index][4].Value = colors[1].R.ToString();
            uniformsTables.Entries[index][5].Value = colors[1].G.ToString();
            uniformsTables.Entries[index][6].Value = colors[1].B.ToString();
            uniformsTables.Entries[index][7].Value = colors[2].R.ToString();
            uniformsTables.Entries[index][8].Value = colors[2].G.ToString();
            uniformsTables.Entries[index][9].Value = colors[2].B.ToString();

            factions[factions.IndexOf(selectedFaction)].Modified = !vanillaBannersTables.Entries[index].SequenceEqual(bannerTables.Entries[index]) || !vanillaUniformsTables.Entries[index].SequenceEqual(uniformsTables.Entries[index]);
        }

        private void CreateModdedPackFile()
        {
            File.Copy("faction_colors.pack", gamePath + "\\data\\faction_colors.pack", true);

            PackFile newPackFile = codec.Open(gamePath + "\\data\\faction_colors.pack");
            DBFile newDb = new DBFile(bannerTables.Header, bannerTables.CurrentType);
            for (int i = 0; i < bannerTables.Entries.Count; i++)
            {
                if (!vanillaBannersTables.Entries[i].SequenceEqual(bannerTables.Entries[i]))
                {
                    newDb.Entries.Add(bannerTables.Entries[i]);
                }
            }
            newPackFile.Files[0].Data = DBDecoder.Encode("faction_banners_tables", newDb);

            newDb = new DBFile(uniformsTables.Header, uniformsTables.CurrentType);
            for (int i = 0; i < uniformsTables.Entries.Count; i++)
            {
                if (!vanillaUniformsTables.Entries[i].SequenceEqual(uniformsTables.Entries[i]))
                {
                    newDb.Entries.Add(uniformsTables.Entries[i]);
                }
            }
            newPackFile.Files[1].Data = DBDecoder.Encode("faction_uniform_colours_tables", newDb);

            codec.Save(newPackFile);
        }

        private string GetGamePath()
        {
            RegistryKey regKey = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (regKey != null)
            {
                string steamPath = regKey.GetValue("SteamPath").ToString();
                //Gets a list of all steam library folder
                string[] configFile = File.ReadAllLines(Path.Combine(steamPath, "config\\config.vdf"));
                List<string> steamLibraryPaths = new List<string>();
                steamLibraryPaths.Add(steamPath.Replace("/", "\\"));
                foreach (var item in configFile)
                {
                    if (item.Contains("BaseInstallFolder"))
                    {
                        steamLibraryPaths.Add(item.Split(new char[] { '"' })[3].Replace("\"", "").Replace("\\\\", "\\"));
                    }
                }
                //Gets a list of all installed Steam games
                foreach (var libraryPath in steamLibraryPaths)
                {
                    foreach (var directory in Directory.GetDirectories(Path.Combine(libraryPath, "SteamApps\\common")))
                    {
                        if (directory.EndsWith("Total War WARHAMMER II")) return directory;
                    }
                }
            }
            return null;
        }

        #endregion
    }
}