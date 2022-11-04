using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Threading;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.IO;
using System.Windows.Threading;
using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ionic.Zip;
using IWshRuntimeLibrary;
using System.Diagnostics;
using Microsoft.Win32;
using System.Xml;

namespace UniversalConverterProjectInstaller
{
    public partial class MainWindow : Window
    {
        public string MessageTitle = "FM Installer";
        public String NewLine = "\\line ";

        public String Bold(String str)
        {
            return "\\b " + str + "\\b0 ";
        }

        public String Green(String str)
        {
            return "{\\cf2 " + str + "}";
        }

        public String Red(String str)
        {
            return "{\\cf3 " + str + "}";
        }

        enum GameVersion
        {
            FM13 = 0,
            FM14 = 1
        }

        public enum InstallationEntryType
        {
            Unknown,
            RemoveFile,
            CopyFile,
            CopyFolder,
            RemoveFolder,
            CreateFolder,
            RemoveFilesByMask,
            UnpackArchive,
            DesktopShortcuts,
            LocaleIni,
            UcpIni,
            CleanDocumentsGraphics,
            CleanDocumentsSavegames,
            GenerateBigIdx
        };

        public enum InstallationTag
        {
            NONE,
            GENERAL,
            PORTRAITS1,
            PORTRAITS2,
            BADGES1,
            BADGES2,
            XXL,
            KITS,
            FACES,
            BANNERS,
            STADIUMS1,
            STADIUMS2
        }

        public class InstallationEntry
        {
            public string mFileName;
            public InstallationEntryType mType;
            public string mDestPath;
            public string mProgressMessage;
            public int mSize;
            public int mPercentagePoints;
            public int mUpdateVersion;
            public InstallationTag mTag;
            public bool mRequiredForFullInstall;
            public bool mRequiredForUpdateInstall;
            public bool mExists;

            public InstallationEntry(string f, InstallationEntryType t, string d, string m, int s, int u, InstallationTag tag = InstallationTag.NONE)
            {
                mFileName = f;
                mType = t;
                mDestPath = d;
                mProgressMessage = m;
                mSize = s;
                mPercentagePoints = 0;
                mUpdateVersion = u;
                mTag = tag;
                mRequiredForFullInstall = false;
                mRequiredForUpdateInstall = false;
                mExists = false;
            }
        };

        int mCurrentInstallFileIndex = 0;
        string mTargetFolder;
        string lang;
        bool initialized = false;
        string mCulture;
        int mGameLanguage;
        int mTheme;
        string mGameDirDefault;
        int mMinUpdate = 0;
        int mMinUpdateOption = 0;
        int mMaxUpdate = 0;
        bool mDebug = false;

        public class InstallFiles
        {
            public bool main = false;
            public bool kits = false;
            public bool faces = false;
            public bool banners = false;
            public bool xxl_portraits = false;
            public int stadiums = 0;
            public int portraits = 0;
            public int badges = 0;
        };

        InstallFiles installFiles = new InstallFiles();

        public class InstallConfig
        {
            public bool installed = false;
            public string version = "";
            public string versionYear = "";
            public int versionUpdate = -1;
            public uint portraits = 0;
            public uint badges = 0;
            public bool xxl_portraits = false;
            public uint stadiums = 0;
            public bool kits = false;
            public bool faces = false;
            public bool banners = false;
        }

        public class UpdateInfo
        {
            public int main = 0;
            public int portraits1 = 0;
            public int portraits2 = 0;
            public int badges1 = 0;
            public int badges2 = 0;
            public int kits = 0;
            public int faces = 0;
            public int banners = 0;
            public int xxl_portraits = 0;
            public int stadiums1 = 0;
            public int stadiums2 = 0;
            public List<String> missingFiles = new List<String>();
        };

        public class InstallXml
        {
            public string version = "-";
            public int update = -1;
            public Dictionary<string, string> variables = new Dictionary<string, string>();
            public List<InstallationEntry> actions = new List<InstallationEntry>();
        }

        InstallConfig installConfig = new InstallConfig();
        InstallXml installXml = new InstallXml();

        public string NodeAttr(XmlNode node, string name, string defValue = "")
        {
            var attr = node.Attributes[name];
            if (attr == null)
                return defValue;
            return attr.InnerText;
        }

        public int ParseInt(string s)
        {
            try
            {
                int number = int.Parse(s);
                return number;
            }
            catch
            {
                return -1;
            }
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern uint GetPrivateProfileInt(string lpAppName, string lpKeyName, int nDefault, string lpFileName);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern int WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

        [DllImport("kernel32.dll")]
        static extern uint GetSystemDefaultLCID();

        List<InstallationEntry> installationEntries = new List<InstallationEntry>();

        private void AddIntallationEntry(ref List<InstallationEntry> entries, InstallationEntry newEntry, ref int totalSize)
        {
            totalSize += newEntry.mSize;
            newEntry.mPercentagePoints = totalSize;
            entries.Add(newEntry);
        }

        public void ParseAction(XmlNode node, int updateVersion, ref List<string> configErrors)
        {
            InstallationEntryType entryType = InstallationEntryType.Unknown;
            string method = NodeAttr(node, "type");
            if (method == "RemoveFile")
                entryType = InstallationEntryType.RemoveFile;
            else if (method == "CopyFile")
                entryType = InstallationEntryType.CopyFile;
            else if (method == "CopyFolder")
                entryType = InstallationEntryType.CopyFolder;
            else if (method == "RemoveFolder")
                entryType = InstallationEntryType.RemoveFolder;
            else if (method == "CreateFolder")
                entryType = InstallationEntryType.CreateFolder;
            else if (method == "RemoveFilesByMask")
                entryType = InstallationEntryType.RemoveFilesByMask;
            else if (method == "UnpackArchive")
                entryType = InstallationEntryType.UnpackArchive;
            else if (method == "DesktopShortcuts")
                entryType = InstallationEntryType.DesktopShortcuts;
            else if (method == "LocaleIni")
                entryType = InstallationEntryType.LocaleIni;
            else if (method == "UcpIni")
                entryType = InstallationEntryType.UcpIni;
            else if (method == "CleanDocumentsGraphics")
                entryType = InstallationEntryType.CleanDocumentsGraphics;
            else if (method == "CleanDocumentsSavegames")
                entryType = InstallationEntryType.CleanDocumentsSavegames;
            else if (method == "GenerateBigIdx")
                entryType = InstallationEntryType.GenerateBigIdx;
            else
                configErrors.Add(string.Format("'action.type' is set to unknown type ({0})", method));
            if (entryType != InstallationEntryType.Unknown)
            {
                InstallationTag tag = InstallationTag.NONE;
                method = NodeAttr(node, "tag");
                if (method == "GENERAL")
                    tag = InstallationTag.GENERAL;
                else if (method == "PORTRAITS1")
                    tag = InstallationTag.PORTRAITS1;
                else if (method == "PORTRAITS2")
                    tag = InstallationTag.PORTRAITS2;
                else if (method == "BADGES1")
                    tag = InstallationTag.BADGES1;
                else if (method == "BADGES2")
                    tag = InstallationTag.BADGES2;
                else if (method == "XXL")
                    tag = InstallationTag.XXL;
                else if (method == "KITS")
                    tag = InstallationTag.KITS;
                else if (method == "FACES")
                    tag = InstallationTag.FACES;
                else if (method == "BANNERS")
                    tag = InstallationTag.BANNERS;
                else if (method == "STADIUMS1")
                    tag = InstallationTag.STADIUMS1;
                else if (method == "STADIUMS2")
                    tag = InstallationTag.STADIUMS2;
                else if (method != "")
                    configErrors.Add(string.Format("'action.tag' is set to unknown tag ({0})", method));
                int size = ParseInt(NodeAttr(node, "size", "0"));
                if (size < -1)
                    configErrors.Add("'action.size' is a negative value");
                string fileName = NodeAttr(node, "path");
                string destPath;
                if (entryType == InstallationEntryType.RemoveFilesByMask)
                    destPath = NodeAttr(node, "mask");
                else
                    destPath = NodeAttr(node, "to");
                string message = NodeAttr(node, "message");
                installXml.actions.Add(new InstallationEntry(fileName, entryType, destPath, message, size, updateVersion, tag));
            }
        }

        public void SetCulture(string culture)
        {
            var vCulture = new CultureInfo(culture);
            Thread.CurrentThread.CurrentCulture = vCulture;
            Thread.CurrentThread.CurrentUICulture = vCulture;
        }

        private void ExitClicked(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public MainWindow()
        {
            List<string> missingFiles = new List<string>();
            List<string> configErrors = new List<string>();
            if (Thread.CurrentThread.CurrentCulture.Name == "uk-UA" && Thread.CurrentThread.CurrentUICulture.Name != "uk-UA")
            {
                SetCulture("uk-UA");
            }
            string configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installer_files\\config\\config.xml");
            if (System.IO.File.Exists(configPath))
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(configPath);
                XmlNode installerNode = doc.DocumentElement;
                if (installerNode != null)
                {
                    installXml.version = NodeAttr(installerNode, "version");
                    if (installXml.version == "")
                    {
                        configErrors.Add("'installer.version' is empty");
                    }
                    installXml.update = ParseInt(NodeAttr(installerNode, "update", "-1"));
                    if (installXml.update < 0 || installXml.update > 98)
                    {
                        configErrors.Add("'installer.update' must be a value between 0 and 98");
                    }
                    foreach (XmlNode node in installerNode.ChildNodes)
                    {
                        if (node.Name == "requirements")
                        {
                            foreach (XmlNode fileNode in node.ChildNodes)
                            {
                                if (fileNode.Name == "file")
                                {
                                    string fileName = NodeAttr(fileNode, "name");
                                    if (fileName != "")
                                    {
                                        string filePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                                        if (!System.IO.File.Exists(filePath))
                                        {
                                            missingFiles.Add(fileName);
                                        }
                                    }
                                }
                            }
                        }
                        else if (node.Name == "variables")
                        {
                            foreach (XmlNode varNode in node.ChildNodes)
                            {
                                if (varNode.Name == "variable")
                                {
                                    installXml.variables[NodeAttr(varNode, "name")] = NodeAttr(varNode, "value");
                                }
                            }
                        }
                        else if (node.Name == "group")
                        {
                            int update = ParseInt(NodeAttr(node, "update", "99"));
                            if (update < 0 || update > 99)
                            {
                                configErrors.Add("'group.update' must be a value between 0 and 99");
                            }
                            foreach (XmlNode compNode in node.ChildNodes)
                            {
                                if (compNode.Name == "action")
                                {
                                    ParseAction(compNode, update, ref configErrors);
                                }
                            }
                        }
                    }
                    string culture = NodeAttr(installerNode, "culture", "");
                    if (culture != "")
                    {
                        SetCulture(culture);
                    }
                    string debug = NodeAttr(installerNode, "debug", "");
                    if (debug == "1" || debug == "true" || debug == "TRUE")
                    {
                        mDebug = true;
                    }
                }
                else
                {
                    configErrors.Add("'installer' node is missing");
                }
            }
            else
            {
                missingFiles.Add("installer_files\\config\\config.xml");
            }
            if (missingFiles.Count > 0)
            {
                string message = languages.Resource.INSTALL_ERROR + "\n";
                if (missingFiles.Count == 1)
                {
                    message += languages.Resource.ERROR_MISSING_FILE;
                }
                else
                {
                    message += languages.Resource.ERROR_MISSING_FILES;
                }
                foreach (string f in missingFiles)
                {
                    message += "\n" + f;
                }
                MessageBox.Show(message, MessageTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            if (configErrors.Count > 0)
            {
                string message = languages.Resource.INSTALL_ERROR + "\n" + languages.Resource.ERROR_CONFIG;
                foreach (string e in configErrors)
                {
                    message += "\n" + e;
                }
                MessageBox.Show(message, MessageTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            if (missingFiles.Count > 0 || configErrors.Count > 0)
            {
                Close();
                return;
            }
            mCulture = Thread.CurrentThread.CurrentUICulture.Name;
            InitializeComponent();
            RbPortraits1.Checked += PortraitsRbChecked;
            RbPortraits2.Checked += PortraitsRbChecked;
            RbBadges1.Checked += BadgesRbChecked;
            RbBadges2.Checked += BadgesRbChecked;
            RbTheme1.Checked += ThemeRbChecked;
            RbTheme2.Checked += ThemeRbChecked;
            ChkXxlPortraits.Checked += ConfigUpdateSizes;
            ChkXxlPortraits.Unchecked += ConfigUpdateSizes;
            ChkKitPack.Checked += ConfigUpdateSizes;
            ChkKitPack.Unchecked += ConfigUpdateSizes;
            ChkFacePack.Checked += ConfigUpdateSizes;
            ChkFacePack.Unchecked += ConfigUpdateSizes;
            ChkBannerPack.Checked += ConfigUpdateSizes;
            ChkBannerPack.Unchecked += ConfigUpdateSizes;
            CbStadiums.SelectionChanged += ConfigUpdateSizes;
            CbPortraits.SelectionChanged += ConfigUpdateSizes;
            CbBadges.SelectionChanged += ConfigUpdateSizes;
            BtnUpdate.Click += ConfigUpdateSizes;
            BtnCleanup.Click += CleanUp;
            CultureInfo ci = CultureInfo.InstalledUICulture;
            lang = ci.ThreeLetterISOLanguageName.ToLower();
            if (mCulture == "de-DE")
            {
                FmVersionEng.Visibility = Visibility.Hidden;
                FmVersionGer.Visibility = Visibility.Visible;
                FmVersionFre.Visibility = Visibility.Hidden;
            }
            else if (mCulture == "fr-FR")
            {
                FmVersionEng.Visibility = Visibility.Hidden;
                FmVersionGer.Visibility = Visibility.Hidden;
                FmVersionFre.Visibility = Visibility.Visible;
            }
            if (installXml.update == 0)
            {
                TbDescription.Text = String.Format(languages.Resource.DESCRIPTION_TEXT, installXml.version);
            }
            else
            {
                TbDescription.Text = String.Format(languages.Resource.DESCRIPTION_TEXT_UPDATE, installXml.version, installXml.update);
            }
            LblVersion.Content = String.Format("{0}.{1}", installXml.version, installXml.update);
            initialized = true;
            // find game dir
            string gameDir = "";
            RegistryKey registryKey13 = Registry.LocalMachine.OpenSubKey("SOFTWARE\\EA SPORTS\\FIFA MANAGER 13");
            if (registryKey13 != null)
            {
                gameDir = (string)registryKey13.GetValue("Install Dir");
                if (!IsCorrectGameFolder(gameDir))
                {
                    gameDir = "";
                }
            }
            if (gameDir == "")
            {
                RegistryKey registryKey14 = Registry.LocalMachine.OpenSubKey("SOFTWARE\\EA SPORTS\\FIFA MANAGER 14");
                if (registryKey14 != null)
                {
                    gameDir = (string)registryKey14.GetValue("Install Dir");
                    if (!IsCorrectGameFolder(gameDir))
                    {
                        gameDir = "";
                    }
                }
            }
            if (gameDir != "")
            {
                mGameDirDefault = gameDir;
            }
        }

        void RemoveFile(string filePath)
        {
            if (System.IO.File.Exists(filePath))
            {
                RemoveReadOnly(filePath);
                System.IO.File.Delete(filePath);
            }
        }

        void RemoveFolder(string folderPath)
        {
            if (Directory.Exists(folderPath))
            {
                try
                {
                    Directory.Delete(folderPath, true);
                }
                catch
                {
                }
            }
        }

        public static string NormalizePath(string path)
        {
            return System.IO.Path.GetFullPath(new Uri(path).LocalPath)
                       .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
                       .ToUpperInvariant();
        }

        private void RemoveReadOnly(string filename)
        {
            if (System.IO.File.Exists(filename))
            {
                System.IO.File.SetAttributes(filename, FileAttributes.Normal);
            }
        }

        private void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
        {
            foreach (DirectoryInfo dir in source.GetDirectories())
                CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
            foreach (FileInfo file in source.GetFiles())
                System.IO.File.Copy(file.FullName, System.IO.Path.Combine(target.FullName, file.Name), true);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void CloseClicked(object sender, RoutedEventArgs e)
        {
            if (TheTab.SelectedIndex == 7)
            {
                if (MessageBox.Show(languages.Resource.INSTALL_ABORT, MessageTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    Close();
                }
            }
            else
            {
                Close();
            }
        }

        private void PortraitsRbChecked(object sender, RoutedEventArgs e)
        {
            if (initialized)
            {
                if (TheTab.SelectedIndex == 3)
                    BtnNext.IsEnabled = true;
            }
        }

        private void BadgesRbChecked(object sender, RoutedEventArgs e)
        {
            if (initialized)
            {
                if (TheTab.SelectedIndex == 4)
                    BtnNext.IsEnabled = true;
            }
        }

        private void ThemeRbChecked(object sender, RoutedEventArgs e)
        {
            if (initialized)
            {
                if (TheTab.SelectedIndex == 5)
                    BtnNext.IsEnabled = true;
            }
        }

        private void ConfigUpdateSizes(object sender, RoutedEventArgs e)
        {
            if (initialized)
                UpdateInstallSizes();
        }

        private void CleanUp(object sender, RoutedEventArgs e)
        {
            string targetFolder = TbInstallDir.Text;
            if (targetFolder.EndsWith("\\") || targetFolder.EndsWith("/"))
                targetFolder = targetFolder.Remove(targetFolder.Length - 1);
            if (string.IsNullOrEmpty(targetFolder))
                return;
            try
            {
                RemoveFolder(targetFolder + "\\" + "custom_pictures");
                RemoveFolder(targetFolder + "\\" + "plugins");
                RemoveFolder(targetFolder + "\\" + "database");
                RemoveFolder(targetFolder + "\\" + "data\\kits");
                RemoveFolder(targetFolder + "\\" + "data\\minikits");
                RemoveFolder(targetFolder + "\\" + "data\\kitarmband");
                RemoveFolder(targetFolder + "\\" + "data\\kitcompbadges");
                RemoveFolder(targetFolder + "\\" + "data\\kitnumbers");
                RemoveFolder(targetFolder + "\\" + "data\\zdata");
                RemoveFolder(targetFolder + "\\" + "data\\assets");
                RemoveFolder(targetFolder + "\\" + "data\\audio\\music");
                RemoveFolder(targetFolder + "\\" + "data\\stadium\\FIFA");
                RemoveFile(targetFolder + "\\" + "Manager13.exe");
                RemoveFile(targetFolder + "\\" + "Manager14.exe");
                RemoveFile(targetFolder + "\\" + "EdManager13.exe");
                RemoveFile(targetFolder + "\\" + "EdManager14.exe");
                RemoveFile(targetFolder + "\\" + "Manager13.ico");
                RemoveFile(targetFolder + "\\" + "Manager14.ico");
                RemoveFile(targetFolder + "\\" + "fmdata\\Restore.dat");
                RemoveFile(targetFolder + "\\" + "fmdata\\Restore.big");
                RemoveFile(targetFolder + "\\" + "fmdata\\UniversalConverterProjectDatabase.ucpdb");
                RemoveFile(targetFolder + "\\" + "fmdata\\UniversalConverterProjectDatabase_WC.ucpdb");

                if (ChkRemoveGraphics.IsChecked == true || ChkRemoveSavegames.IsChecked == true)
                {
                    var docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (ChkRemoveGraphics.IsChecked == true)
                        RemoveFolder(System.IO.Path.Combine(docsDir, "FM\\Graphics"));
                    if (ChkRemoveSavegames.IsChecked == true)
                        RemoveFolder(System.IO.Path.Combine(docsDir, "FM\\Data\\SaveGames"));
                }
            }
            catch
            {
            }
        }

        private long GetTotalFreeSpace(string driveName)
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.Name == driveName)
                {
                    return drive.AvailableFreeSpace;
                }
            }
            return -1;
        }

        private void UpdateInstallSizes()
        {
            double generalSize = 0.0;
            double portraitsSize = 0.0;
            double badgesSize = 0.0;
            double xxlPortraitsSize = 0.0;
            double kitPackSize = 0.0;
            double facePackSize = 0.0;
            double bannerPackSize = 0.0;
            double stadiumPackSize = 0.0;
            foreach (var a in installXml.actions)
            {
                if (a.mSize > 0)
                {
                    if ((a.mUpdateVersion >= mMinUpdate && a.mUpdateVersion <= mMaxUpdate) || a.mUpdateVersion == 99)
                    {
                        switch (a.mTag)
                        {
                            case InstallationTag.PORTRAITS1:
                                if (CbPortraits.SelectedIndex == 0)
                                {
                                    portraitsSize += a.mSize;
                                }
                                break;
                            case InstallationTag.PORTRAITS2:
                                if (CbPortraits.SelectedIndex == 1)
                                {
                                    portraitsSize += a.mSize;
                                }
                                break;
                            case InstallationTag.BADGES1:
                                if (CbBadges.SelectedIndex == 0)
                                {
                                    badgesSize += a.mSize;
                                }
                                break;
                            case InstallationTag.BADGES2:
                                if (CbBadges.SelectedIndex == 1)
                                {
                                    badgesSize += a.mSize;
                                }
                                break;
                            case InstallationTag.XXL:
                                if (ChkXxlPortraits.IsChecked == true)
                                {
                                    xxlPortraitsSize += a.mSize;
                                }
                                break;
                            case InstallationTag.KITS:
                                if (ChkKitPack.IsChecked == true)
                                {
                                    kitPackSize += a.mSize;
                                }
                                break;
                            case InstallationTag.FACES:
                                if (ChkFacePack.IsChecked == true)
                                {
                                    facePackSize += a.mSize;
                                }
                                break;
                            case InstallationTag.BANNERS:
                                if (ChkBannerPack.IsChecked == true)
                                {
                                    bannerPackSize += a.mSize;
                                }
                                break;
                            case InstallationTag.STADIUMS1:
                                if (CbStadiums.SelectedIndex == 1 || CbStadiums.SelectedIndex == 2)
                                {
                                    stadiumPackSize += a.mSize;
                                }
                                break;
                            case InstallationTag.STADIUMS2:
                                if (CbStadiums.SelectedIndex == 2)
                                {
                                    stadiumPackSize += a.mSize;
                                }
                                break;
                            default:
                                if (a.mType == InstallationEntryType.CleanDocumentsGraphics)
                                {
                                    if (ChkRemoveGraphics.IsChecked == true)
                                    {
                                        generalSize += a.mSize;
                                    }
                                }
                                else if (a.mType == InstallationEntryType.CleanDocumentsSavegames)
                                {
                                    if (ChkRemoveSavegames.IsChecked == true)
                                    {
                                        generalSize += a.mSize;
                                    }
                                }
                                else
                                {
                                    generalSize += a.mSize;
                                }
                                break;
                        }
                    }
                }
            }
            generalSize = Math.Ceiling(generalSize / 1024.0 * 100.0) / 100.0;
            portraitsSize = Math.Ceiling(portraitsSize / 1024.0 * 100.0) / 100.0;
            badgesSize = Math.Ceiling(badgesSize / 1024.0 * 100.0) / 100.0;
            xxlPortraitsSize = Math.Ceiling(xxlPortraitsSize / 1024.0 * 100.0) / 100.0;
            kitPackSize = Math.Ceiling(kitPackSize / 1024.0 * 100.0) / 100.0;
            facePackSize = Math.Ceiling(facePackSize / 1024.0 * 100.0) / 100.0;
            bannerPackSize = Math.Ceiling(bannerPackSize / 1024.0 * 100.0) / 100.0;
            stadiumPackSize = Math.Ceiling(stadiumPackSize / 1024.0 * 100.0) / 100.0;
            LblPortraitsSize.Content = portraitsSize.ToString("0.00") + " GB";
            LblBadgesSize.Content = badgesSize.ToString("0.00") + " GB";
            LblXxlPortraitsSize.Content = xxlPortraitsSize.ToString("0.00") + " GB";
            LblKitPackSize.Content = kitPackSize.ToString("0.00") + " GB";
            LblFacePackSize.Content = facePackSize.ToString("0.00") + " GB";
            LblBannerPackSize.Content = bannerPackSize.ToString("0.00") + " GB";
            LblStadiumPackSize.Content = stadiumPackSize.ToString("0.00") + " GB";
            double totalSize = generalSize + portraitsSize + badgesSize + xxlPortraitsSize + kitPackSize + facePackSize + bannerPackSize + stadiumPackSize;
            LblRequiredSpace.Content = totalSize.ToString("0.00") + " GB";
            string driveName = "";
            if (TbInstallDir.Text.Length > 0)
            {
                FileInfo file = new FileInfo(TbInstallDir.Text);
                DriveInfo drive = new DriveInfo(file.Directory.Root.FullName);
                driveName = drive.Name;
            }
            double availableSpace = (double)GetTotalFreeSpace(driveName) / 1024.0 / 1024.0 / 1024.0;
            LblAvailableSpace.Content = availableSpace.ToString("0.00") + " GB";
            if (totalSize > availableSpace)
            {
                BtnForward.IsEnabled = true;
                BtnOk.IsEnabled = false;
                TxInstallStart.Text = languages.Resource.INSTALL_NO_SPACE;
            }
            else
            {
                BtnForward.IsEnabled = false;
                BtnOk.IsEnabled = true;
                TxInstallStart.Text = languages.Resource.INSTALLDIR_START;
            }
        }

        private void NextClicked(object sender, RoutedEventArgs e)
        {
            int index = TheTab.SelectedIndex;
            int targetScreen = index + 1;

            // Closing screen

            if (index == 2) // Install dir screen > (Portraits, Badges or Config screen)
            {
                targetScreen = 6;
                if (CbInstallType.SelectedIndex == 0) // Full
                {
                    if (installFiles.portraits == 3)
                        targetScreen = 3; // Portraits
                    else if (installFiles.badges == 3)
                        targetScreen = 4; // Badges
                    else
                        targetScreen = 5; // Theme
                }
                try
                {
                    for (int i = 0; i < installXml.actions.Count; i++)
                    {
                        if (installXml.actions[i].mSize == -1)
                        {
                            if (installXml.actions[i].mType == InstallationEntryType.UnpackArchive)
                            {
                                double fileSize = 0.0;
                                string zipFileName = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, installXml.actions[i].mFileName);
                                if (System.IO.File.Exists(zipFileName))
                                {
                                    using (ZipFile zip = ZipFile.Read(zipFileName))
                                    {
                                        foreach (ZipEntry zipEntry in zip)
                                        {
                                            if (!zipEntry.IsDirectory)
                                                fileSize += zipEntry.UncompressedSize;
                                        }
                                    }
                                }
                                installXml.actions[i].mSize = (int)Math.Ceiling(fileSize / 1048576.0);
                            }
                            else
                            {
                                installXml.actions[i].mSize = 0;
                            }
                        }
                    }
                }
                catch (Exception exc)
                {
                    MessageBox.Show(String.Format("{0}\nDuring archive size calculation\n{1}", languages.Resource.INSTALL_ERROR, exc.Message), MessageTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                    return;
                }
                mMinUpdate = (CbInstallType.SelectedIndex == 1) ? mMinUpdateOption : 0;
                mMaxUpdate = installXml.update;
            }
            else if (index == 3) // Portraits screen > (Badges or Config screen)
            {
                targetScreen = 5;
                if (installFiles.badges == 3)
                    targetScreen = 4;
            }
            else if (index == 7) // last screen
            {
                Close();
                return;
            }

            // Opening screen

            TheTab.SelectedIndex = targetScreen;
            index = TheTab.SelectedIndex;
            if (index == 1) // License screen
            {
                BtnNext.Visibility = Visibility.Hidden;
                BtnOk.Visibility = Visibility.Visible;
                BtnOk.IsEnabled = false;
            }
            if (index == 2) // Install dir screen
            {
                BtnNext.Visibility = Visibility.Visible;
                BtnOk.Visibility = Visibility.Hidden;
                BtnNext.IsEnabled = false;
                if (mGameDirDefault != "")
                {
                    TbInstallDir.Text = mGameDirDefault;
                }
            }
            else if (index == 3) // Portraits screen
            {
                TbPortraits1.Text = String.Format(languages.Resource.INSTALLOPTION_PORTRAITS_1_INFO1, installXml.variables["numPortraits1"]);
                TbPortraits2.Text = String.Format(languages.Resource.INSTALLOPTION_PORTRAITS_2_INFO1, installXml.variables["numPortraits2"]);
                BtnNext.IsEnabled = (RbPortraits1.IsChecked == true || RbPortraits2.IsChecked == true);
            }
            else if (index == 4) // Badges screen
            {
                TbBadges1.Text = String.Format(languages.Resource.INSTALLOPTION_BADGES_1_INFO1, installXml.variables["numBadges1"]);
                TbBadges2.Text = String.Format(languages.Resource.INSTALLOPTION_BADGES_2_INFO1, installXml.variables["numBadges2"]);
                BtnNext.IsEnabled = (RbBadges1.IsChecked == true || RbBadges2.IsChecked == true);
            }
            else if (index == 5) // Theme screen
            {
                BtnNext.IsEnabled = (RbTheme1.IsChecked == true || RbTheme2.IsChecked == true);
            }
            else if (index == 6) // Config
            {
                ((ComboBoxItem)CbStadiums.Items.GetItemAt(1)).Content = String.Format(languages.Resource.INSTALLOPTION_STADIUMS_2, installXml.variables["numStadiums1"]);
                ((ComboBoxItem)CbStadiums.Items.GetItemAt(2)).Content = String.Format(languages.Resource.INSTALLOPTION_STADIUMS_3, installXml.variables["numStadiums2"]);
                BtnNext.Visibility = Visibility.Hidden;
                BtnOk.Visibility = Visibility.Visible;
                BtnForward.Visibility = Visibility.Visible;
                if (CbInstallType.SelectedIndex == 0) // Full
                {
                    if (installFiles.portraits != 0)
                    {
                        CbPortraits.SelectedIndex = (RbPortraits2.IsChecked == true) ? 1 : 0;
                    }
                    else
                    {
                        CbPortraits.SelectedIndex = -1;
                    }
                    CbPortraits.IsEnabled = (installFiles.portraits == 3);
                    if (installFiles.badges != 0)
                    {
                        CbBadges.SelectedIndex = (RbBadges2.IsChecked == true) ? 1 : 0;
                    }
                    else
                    {
                        CbBadges.SelectedIndex = -1;
                    }
                    CbBadges.IsEnabled = (installFiles.badges == 3);
                    ChkXxlPortraits.IsChecked = (installFiles.xxl_portraits ? true : false);
                    ChkXxlPortraits.IsEnabled = (installFiles.xxl_portraits ? true : false);
                    ChkKitPack.IsChecked = (installFiles.kits ? true : false);
                    ChkKitPack.IsEnabled = (installFiles.kits ? true : false);
                    ChkFacePack.IsChecked = (installFiles.faces ? true : false);
                    ChkFacePack.IsEnabled = (installFiles.faces ? true : false);
                    ChkBannerPack.IsChecked = (installFiles.banners ? true : false);
                    ChkBannerPack.IsEnabled = (installFiles.banners ? true : false);
                    CbStadiums.SelectedIndex = installFiles.stadiums;
                    CbStadiums.IsEnabled = installFiles.stadiums != 0;
                    if (installFiles.stadiums == 1)
                    {
                        var item = CbStadiums.Items.GetItemAt(2) as ComboBoxItem;
                        item.IsEnabled = false;
                    }
                    BtnCleanup.IsEnabled = true;
                    ChkRemoveGraphics.IsChecked = true;
                    CbTheme.SelectedIndex = (RbTheme2.IsChecked == true) ? 1 : 0;
                }
                else
                {
                    if (installConfig.portraits != 0)
                    {
                        CbPortraits.SelectedIndex = (installConfig.portraits == 2) ? 1 : 0;
                    }
                    else
                    {
                        CbPortraits.SelectedIndex = -1;
                    }
                    CbPortraits.IsEnabled = false;
                    if (installConfig.badges != 0)
                    {
                        CbBadges.SelectedIndex = (installConfig.badges == 2) ? 1 : 0;
                    }
                    else
                    {
                        CbBadges.SelectedIndex = -1;
                    }
                    CbBadges.IsEnabled = false;
                    ChkXxlPortraits.IsChecked = (installConfig.xxl_portraits ? true : false);
                    ChkXxlPortraits.IsEnabled = false;
                    ChkKitPack.IsChecked = (installConfig.kits ? true : false);
                    ChkKitPack.IsEnabled = false;
                    ChkFacePack.IsChecked = (installConfig.faces ? true : false);
                    ChkFacePack.IsEnabled = false;
                    ChkBannerPack.IsChecked = (installConfig.banners ? true : false);
                    ChkBannerPack.IsEnabled = false;
                    if (installConfig.stadiums <= 2)
                        CbStadiums.SelectedIndex = (int)installConfig.stadiums;
                    else
                        CbStadiums.SelectedIndex = 0;
                    CbStadiums.IsEnabled = false;
                    BtnCleanup.IsEnabled = false;
                    ChkRemoveGraphics.IsChecked = false;

                    string localeSrcFile = System.IO.Path.Combine(TbInstallDir.Text, "locale.ini");
                    if (System.IO.File.Exists(localeSrcFile))
                    {
                        StringBuilder sb = new StringBuilder(256);
                        GetPrivateProfileString("OPTIONS", "TEXT_LANGUAGE", lang, sb, sb.Capacity, localeSrcFile);
                        lang = sb.ToString();
                    }

                    var docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    if (!string.IsNullOrEmpty(docsDir))
                    {
                        string ucpSettingsFile = System.IO.Path.Combine(TbInstallDir.Text, "FM\\Config\\ucp.ini");
                        if (System.IO.File.Exists(ucpSettingsFile))
                        {
                            StringBuilder sb = new StringBuilder(256);
                            GetPrivateProfileString("MAIN", "THEME", lang, sb, sb.Capacity, ucpSettingsFile);
                            CbTheme.SelectedIndex = sb.ToString().ToLower() == "dark" ? 1 : 0;
                        }
                    }
                }
                ChkRemoveGraphics.IsEnabled = CbInstallType.SelectedIndex == 0;
                int targetLanguage = 2;
                if (lang == "eng")
                    targetLanguage = 2;
                else if (lang == "ger" || lang == "deu")
                    targetLanguage = 1;
                else if (lang == "fre" || lang == "fra")
                    targetLanguage = 4;
                else if (lang == "ita")
                    targetLanguage = 5;
                else if (lang == "spa")
                    targetLanguage = 3;
                else if (lang == "pol")
                    targetLanguage = 7;
                else if (lang == "rus")
                {
                    if (CbInstallType.SelectedIndex == 0 && (mCulture == "uk" || mCulture == "uk-UA"))
                    {
                        targetLanguage = 11;
                    }
                    else
                        targetLanguage = 10;
                }
                else if (lang == "bel" || lang == "kaz" || lang == "aze")
                    targetLanguage = 10;
                else if (lang == "ukr")
                    targetLanguage = 11;
                else if (lang == "por")
                    targetLanguage = 8;
                else if (lang == "cze" || lang == "ces")
                    targetLanguage = 0;
                else if (lang == "tur")
                    targetLanguage = 9;
                else if (lang == "hun")
                    targetLanguage = 6;
                else if (lang == "chi" || lang == "zho")
                    targetLanguage = 13;
                else if (lang == "kor")
                    targetLanguage = 12;
                CbGameLanguage.SelectedIndex = targetLanguage;
                UpdateInstallSizes();
            }
            else if (index == 7) // last screen
            {
                BtnOk.IsEnabled = false;
                BtnForward.Visibility = Visibility.Hidden;
                BtnX.Visibility = Visibility.Visible;
                BtnX.IsEnabled = false;
                mTargetFolder = TbInstallDir.Text;
                mGameLanguage = CbGameLanguage.SelectedIndex;
                mTheme = CbTheme.SelectedIndex;
                if (mTargetFolder.EndsWith("\\") || mTargetFolder.EndsWith("/"))
                    mTargetFolder = mTargetFolder.Remove(mTargetFolder.Length - 1);
                TxDebug.Visibility = mDebug ? Visibility.Visible : Visibility.Hidden;
                int totalSize = 0;
                foreach (var a in installXml.actions)
                {
                    if ((a.mUpdateVersion >= mMinUpdate && a.mUpdateVersion <= mMaxUpdate) || a.mUpdateVersion == 99)
                    {
                        switch (a.mTag)
                        {
                            case InstallationTag.PORTRAITS1:
                                if (CbPortraits.SelectedIndex == 0)
                                {
                                    AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                }
                                break;
                            case InstallationTag.PORTRAITS2:
                                if (CbPortraits.SelectedIndex == 1)
                                {
                                    AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                }
                                break;
                            case InstallationTag.BADGES1:
                                if (CbBadges.SelectedIndex == 0)
                                {
                                    AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                }
                                break;
                            case InstallationTag.BADGES2:
                                if (CbBadges.SelectedIndex == 1)
                                {
                                    AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                }
                                break;
                            case InstallationTag.XXL:
                                if (ChkXxlPortraits.IsChecked == true)
                                {
                                    AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                }
                                break;
                            case InstallationTag.KITS:
                                if (ChkKitPack.IsChecked == true)
                                {
                                    AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                }
                                break;
                            case InstallationTag.FACES:
                                if (ChkFacePack.IsChecked == true)
                                {
                                    AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                }
                                break;
                            case InstallationTag.BANNERS:
                                if (ChkBannerPack.IsChecked == true)
                                {
                                    AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                }
                                break;
                            case InstallationTag.STADIUMS1:
                                if (CbStadiums.SelectedIndex == 1 || CbStadiums.SelectedIndex == 2)
                                {
                                    AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                }
                                break;
                            case InstallationTag.STADIUMS2:
                                if (CbStadiums.SelectedIndex == 2)
                                {
                                    AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                }
                                break;
                            default:
                                if (a.mType == InstallationEntryType.CleanDocumentsGraphics)
                                {
                                    if (ChkRemoveGraphics.IsChecked == true)
                                    {
                                        AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                    }
                                }
                                else if (a.mType == InstallationEntryType.CleanDocumentsSavegames)
                                {
                                    if (ChkRemoveSavegames.IsChecked == true)
                                    {
                                        AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                    }
                                }
                                else
                                {
                                    AddIntallationEntry(ref installationEntries, a, ref totalSize);
                                }
                                break;
                        }
                    }
                }
                PbInstallation.Maximum = totalSize;
                TbInstallation.Text = installationEntries[mCurrentInstallFileIndex].mProgressMessage;
                if (CbInstallType.SelectedIndex == 0 && !mDebug)
                {
                    RemoveFile(System.IO.Path.Combine(mTargetFolder, "installer.ini"));
                }
                unpackWorker = new BackgroundWorker();
                unpackWorker.WorkerReportsProgress = true;
                unpackWorker.DoWork += worker_DoWork;
                unpackWorker.ProgressChanged += worker_ProgressChanged;
                unpackWorker.RunWorkerCompleted += worker_RunWorkerCompleted;
                unpackWorker.RunWorkerAsync();
            }
        }

        private static BackgroundWorker unpackWorker = null;

        private static void ReportProgress(long lastProgress, long possibleProgress, long currentPoints, long totalPoints)
        {
            long nextProgress = lastProgress + possibleProgress;
            long currProgress = lastProgress + (long)((float)currentPoints / (float)totalPoints * possibleProgress);
            if (currProgress > nextProgress)
                currProgress = nextProgress;
            unpackWorker.ReportProgress((int)currProgress);
            Thread.Sleep(0);
        }

        private static long unpack_lastProgress = 0;
        private static long unpack_possibleProgress = 0;
        private static long unpack_totalSizeUnpacked = 0;
        private static long unpack_totalSize = 0;

        private static void UnpackZipProgress(object sender, ExtractProgressEventArgs args)
        {
            if (args.EventType == ZipProgressEventType.Extracting_EntryBytesWritten
                || args.EventType == ZipProgressEventType.Extracting_BeforeExtractEntry
                || args.EventType == ZipProgressEventType.Extracting_AfterExtractEntry)
            {
                ReportProgress(unpack_lastProgress, unpack_possibleProgress, unpack_totalSizeUnpacked + args.BytesTransferred, unpack_totalSize);
            }
        }

        private void worker_DoWork(object sender, DoWorkEventArgs e)
        {
            for (int i = 0; i < installationEntries.Count; i++)
            {
                if (!mDebug)
                {
                    if (installationEntries[i].mType == InstallationEntryType.CopyFolder)
                    {
                        string source = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, installationEntries[i].mFileName);
                        string target = System.IO.Path.Combine(mTargetFolder, installationEntries[i].mDestPath);
                        CopyFilesRecursively(new DirectoryInfo(source), new DirectoryInfo(target));
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.RemoveFile)
                    {
                        string filePath = System.IO.Path.Combine(mTargetFolder, installationEntries[i].mDestPath);
                        if (System.IO.File.Exists(filePath))
                        {
                            RemoveReadOnly(filePath);
                            System.IO.File.Delete(filePath);
                        }
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.CopyFile)
                    {
                        string srcFilePath = System.IO.Path.Combine(mTargetFolder, installationEntries[i].mFileName);
                        if (System.IO.File.Exists(srcFilePath))
                        {
                            string dstFilePath = System.IO.Path.Combine(mTargetFolder, installationEntries[i].mDestPath);
                            if (System.IO.File.Exists(dstFilePath))
                            {
                                RemoveReadOnly(dstFilePath);
                                System.IO.File.Delete(dstFilePath);
                            }
                            System.IO.File.Copy(srcFilePath, dstFilePath, true);
                        }
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.RemoveFolder)
                    {
                        string folderPath = System.IO.Path.Combine(mTargetFolder, installationEntries[i].mDestPath);
                        if (Directory.Exists(folderPath))
                        {
                            Directory.Delete(folderPath, true);
                        }
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.CreateFolder)
                    {
                        string folderPath = System.IO.Path.Combine(mTargetFolder, installationEntries[i].mDestPath);
                        Directory.CreateDirectory(folderPath);
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.RemoveFilesByMask)
                    {
                        string dirPath = System.IO.Path.Combine(mTargetFolder, installationEntries[i].mFileName);
                        if (Directory.Exists(dirPath))
                        {
                            var dir = new DirectoryInfo(dirPath);
                            var files = dir.GetFiles(installationEntries[i].mDestPath);
                            foreach (var file in files)
                            {
                                RemoveReadOnly(file.FullName);
                                file.Delete();
                            }
                        }
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.UnpackArchive)
                    {
                        string zipFileName = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, installationEntries[i].mFileName);
                        if (System.IO.File.Exists(zipFileName))
                        {
                            using (ZipFile zip = ZipFile.Read(zipFileName))
                            {
                                unpack_totalSize = 0;
                                foreach (ZipEntry zipEntry in zip)
                                {
                                    if (!zipEntry.IsDirectory)
                                        unpack_totalSize += zipEntry.UncompressedSize;
                                }
                                unpack_lastProgress = 0;
                                if (i > 0)
                                    unpack_lastProgress = installationEntries[i - 1].mPercentagePoints;
                                unpack_possibleProgress = installationEntries[i].mPercentagePoints - unpack_lastProgress;
                                unpack_totalSizeUnpacked = 0;
                                zip.ExtractProgress += UnpackZipProgress;
                                foreach (ZipEntry zipEntry in zip)
                                {
                                    zipEntry.Extract(mTargetFolder, ExtractExistingFileAction.OverwriteSilently);
                                    unpack_totalSizeUnpacked += zipEntry.UncompressedSize;
                                }
                            }
                        }
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.LocaleIni)
                    {
                        string localeDstFile = System.IO.Path.Combine(mTargetFolder, "locale.ini");
                        if (System.IO.File.Exists(localeDstFile))
                        {
                            int gameLanguage = mGameLanguage;
                            string targetLang = "eng";
                            if (gameLanguage == 2)
                                targetLang = "eng";
                            else if (gameLanguage == 1)
                                targetLang = "ger";
                            else if (gameLanguage == 4)
                                targetLang = "fre";
                            else if (gameLanguage == 5)
                                targetLang = "ita";
                            else if (gameLanguage == 3)
                                targetLang = "spa";
                            else if (gameLanguage == 7)
                                targetLang = "pol";
                            else if (gameLanguage == 10)
                                targetLang = "rus";
                            else if (gameLanguage == 11)
                                targetLang = "ukr";
                            else if (gameLanguage == 8)
                                targetLang = "por";
                            else if (gameLanguage == 0)
                                targetLang = "cze";
                            else if (gameLanguage == 9)
                                targetLang = "tur";
                            else if (gameLanguage == 6)
                                targetLang = "hun";
                            else if (gameLanguage == 13)
                                targetLang = "chi";
                            else if (gameLanguage == 12)
                                targetLang = "kor";

                            StringBuilder sb = new StringBuilder(256);
                            GetPrivateProfileString("OPTIONS", "TEXT_LANGUAGE", "", sb, sb.Capacity, localeDstFile);
                            string currLang = sb.ToString();

                            //MessageBox.Show(targetLang);

                            if (targetLang != currLang)
                            {
                                WritePrivateProfileString("OPTIONS", "TEXT_LANGUAGE", targetLang, localeDstFile);

                                List<string> availableLangs = new List<string>(new string[] { "brp", "dan", "dut", "eng", "fre", "ger", "gre",
                                "ita", "kor", "nor", "pol", "por", "spa", "swe" });

                                if (availableLangs.Contains(targetLang)
                                    && System.IO.File.Exists(System.IO.Path.Combine(mTargetFolder, "data\\cmn\\fe\\" + targetLang + ".db"))
                                    && System.IO.File.Exists(System.IO.Path.Combine(mTargetFolder, "data\\fi-audio\\dat_" + targetLang + ".big")))
                                {
                                    WritePrivateProfileString("", "TEXT_LANGUAGE_OVERRIDE", targetLang, localeDstFile);
                                }
                                if (targetLang == "chi" || targetLang == "kor")
                                {
                                    string fontsSrc = System.IO.Path.Combine(mTargetFolder, "fmdata\\" + targetLang + "\\fonts.big");
                                    if (System.IO.File.Exists(fontsSrc))
                                    {
                                        System.IO.File.Copy(fontsSrc, System.IO.Path.Combine(mTargetFolder, "fonts.big"), true);
                                    }
                                }
                            }
                        }
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.UcpIni)
                    {
                        {
                            var docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                            if (!string.IsNullOrEmpty(docsDir) && Directory.Exists(docsDir))
                            {
                                string ucpLaunchedFile = System.IO.Path.Combine(docsDir, "FM\\Config\\ucp-launched");
                                if (System.IO.File.Exists(ucpLaunchedFile))
                                {
                                    RemoveReadOnly(ucpLaunchedFile);
                                    System.IO.File.Delete(ucpLaunchedFile);
                                }
                            }
                        }
                        if (mTheme == 1)
                        {
                            var docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.Create);
                            if (!string.IsNullOrEmpty(docsDir))
                            {
                                string configDir = System.IO.Path.Combine(docsDir, "FM\\Config");
                                Directory.CreateDirectory(configDir);
                                string ucpSettingsFile = System.IO.Path.Combine(configDir, "ucp.ini");
                                if (System.IO.File.Exists(ucpSettingsFile))
                                {
                                    RemoveReadOnly(ucpSettingsFile);
                                    System.IO.File.Delete(ucpSettingsFile);
                                }
                                string srcFilePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installer_files\\themes\\dark\\ucp.ini");
                                if (System.IO.File.Exists(srcFilePath))
                                {
                                    System.IO.File.Copy(srcFilePath, ucpSettingsFile, true);
                                }
                            }
                        }
                        else
                        {
                            var docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                            if (!string.IsNullOrEmpty(docsDir) && Directory.Exists(docsDir))
                            {
                                string ucpSettingsFile = System.IO.Path.Combine(docsDir, "FM\\Config\\ucp.ini");
                                if (System.IO.File.Exists(ucpSettingsFile))
                                {
                                    RemoveReadOnly(ucpSettingsFile);
                                    System.IO.File.Delete(ucpSettingsFile);
                                }
                            }
                        }
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.DesktopShortcuts)
                    {
                        object shDesktop = (object)"Desktop";
                        WshShell shell = new WshShell();
                        string managerAddress = (string)shell.SpecialFolders.Item(ref shDesktop) + "\\FIFA Manager 2022.lnk";
                        IWshShortcut managerShortcut = (IWshShortcut)shell.CreateShortcut(managerAddress);
                        if (lang == "ger" || lang == "deu")
                            managerShortcut.Description = "Fussball Manager 2022";
                        else if (lang == "fre" || lang == "fra")
                            managerShortcut.Description = "LFP Manager 2022";
                        else
                            managerShortcut.Description = "FIFA Manager 2022";
                        managerShortcut.TargetPath = mTargetFolder + "\\Manager.exe";
                        managerShortcut.WorkingDirectory = mTargetFolder;
                        managerShortcut.Save();
                        string editorAddress = (string)shell.SpecialFolders.Item(ref shDesktop) + "\\Editor 2022.lnk";
                        IWshShortcut editorShortcut = (IWshShortcut)shell.CreateShortcut(editorAddress);
                        if (lang == "fre" || lang == "fra")
                            editorShortcut.Description = "Éditeur 2022";
                        if (lang == "rus" || lang == "ukr" || lang == "bel" || lang == "kaz" || lang == "aze")
                            editorShortcut.Description = "Редактор 2022";
                        else
                            editorShortcut.Description = "Editor 2022";
                        editorShortcut.TargetPath = mTargetFolder + "\\EdManager.exe";
                        editorShortcut.WorkingDirectory = mTargetFolder;
                        editorShortcut.Save();
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.CleanDocumentsGraphics)
                    {
                        var docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        if (!string.IsNullOrEmpty(docsDir))
                            RemoveFolder(System.IO.Path.Combine(docsDir, "FM\\Graphics"));
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.CleanDocumentsSavegames)
                    {
                        var docsDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        if (!string.IsNullOrEmpty(docsDir))
                            RemoveFolder(System.IO.Path.Combine(docsDir, "FM\\Data\\SaveGames"));
                    }
                    else if (installationEntries[i].mType == InstallationEntryType.GenerateBigIdx)
                    {
                        string bigIdxGenPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installer_files\\tools\\gen_big_idx.exe");
                        if (System.IO.File.Exists(bigIdxGenPath))
                        {
                            string gameVersion = "13";
                            if (System.IO.File.Exists(System.IO.Path.Combine(mTargetFolder, "data\\zdata_48.big")))
                                gameVersion = "14";
                            Process myProcess = new Process();
                            myProcess.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                            myProcess.StartInfo.CreateNoWindow = true;
                            myProcess.StartInfo.UseShellExecute = false;
                            myProcess.StartInfo.FileName = bigIdxGenPath;
                            myProcess.StartInfo.Arguments = "\"" + mTargetFolder + "\" -game " + gameVersion;
                            myProcess.EnableRaisingEvents = true;
                            //myProcess.Exited += new EventHandler(process_Exited);
                            myProcess.Start();
                            myProcess.WaitForExit();
                            //ExitCode = myProcess.ExitCode;
                        }
                    }
                }
                mCurrentInstallFileIndex++;
                (sender as BackgroundWorker).ReportProgress(installationEntries[i].mPercentagePoints);
                Thread.Sleep(50);
            }
        }

        private void worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            PbInstallation.Value = e.ProgressPercentage;
            if (mCurrentInstallFileIndex < installationEntries.Count)
            {
                TbInstallation.Text = installationEntries[mCurrentInstallFileIndex].mProgressMessage;
                if (mDebug)
                {
                    TxDebug.Text += installationEntries[mCurrentInstallFileIndex].mProgressMessage + "\n";
                }
            }
        }

        private void worker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            // First, handle the case where an exception was thrown.
            string iniFileName = System.IO.Path.Combine(mTargetFolder, "installer.ini");
            if (e.Error != null)
            {
                PbInstallation.Visibility = Visibility.Hidden;
                TbCompleted.Text = languages.Resource.INSTALL_ERROR;
                TbInstallation.Text += ": " + e.Error.Message;
                TbCompleted.Visibility = Visibility.Visible;
                BtnOk.IsEnabled = false;
                BtnX.IsEnabled = true;
                if (!mDebug)
                {
                    if (!installConfig.installed)
                    {
                        WritePrivateProfileString("MAIN", "Installed", "0", iniFileName);
                        WritePrivateProfileString("MAIN", "Version", String.Format("{0}.{1}", installXml.version, installXml.update), iniFileName);
                    }
                    WritePrivateProfileString("MAIN", "Error", "1", iniFileName);
                    WritePrivateProfileString("MAIN", "ErrorVersion", String.Format("{0}.{1}", installXml.version, installXml.update), iniFileName);
                    WritePrivateProfileString("MAIN", "ErrorMessage", e.Error.Message, iniFileName);
                }
            }
            else
            {
                PbInstallation.Visibility = Visibility.Hidden;
                TbInstallation.Text = languages.Resource.INSTALL_CLOSE;
                TbCompleted.Visibility = Visibility.Visible;
                BtnOk.IsEnabled = true;
                BtnX.IsEnabled = false;
                if (!mDebug)
                {
                    WritePrivateProfileString("MAIN", "Installed", "1", iniFileName);
                    WritePrivateProfileString("MAIN", "Version", String.Format("{0}.{1}", installXml.version, installXml.update), iniFileName);
                    WritePrivateProfileString("MAIN", "Error", "0", iniFileName);
                    WritePrivateProfileString("MAIN", "ErrorVersion", "", iniFileName);
                    WritePrivateProfileString("MAIN", "ErrorMessage", "", iniFileName);
                }
            }
            if (!mDebug)
            {
                int portraitsType = CbPortraits.SelectedIndex + 1;
                WritePrivateProfileString("MAIN", "Portraits", portraitsType.ToString(), iniFileName);
                int badgesType = CbBadges.SelectedIndex + 1;
                WritePrivateProfileString("MAIN", "Badges", badgesType.ToString(), iniFileName);
                WritePrivateProfileString("MAIN", "XxlPortraits", (ChkXxlPortraits.IsChecked == true) ? "1" : "0", iniFileName);
                WritePrivateProfileString("MAIN", "Kits", (ChkKitPack.IsChecked == true) ? "1" : "0", iniFileName);
                WritePrivateProfileString("MAIN", "Faces", (ChkFacePack.IsChecked == true) ? "1" : "0", iniFileName);
                WritePrivateProfileString("MAIN", "Banners", (ChkBannerPack.IsChecked == true) ? "1" : "0", iniFileName);
                int stadiumsInstallType = CbStadiums.SelectedIndex;
                WritePrivateProfileString("MAIN", "Stadiums", stadiumsInstallType.ToString(), iniFileName);
            }
        }

        private void LicenseChecked(object sender, RoutedEventArgs e)
        {
            BtnOk.IsEnabled = ChkAcceptLicense.IsChecked == true;
        }

        private bool IsCorrectGameFolder(string dir)
        {
            if (dir != "")
            {
                if (Directory.Exists(dir)
                    && Directory.Exists(System.IO.Path.Combine(dir, "fmdata"))
                    && Directory.Exists(System.IO.Path.Combine(dir, "data"))
                    && System.IO.File.Exists(System.IO.Path.Combine(dir, "GfxCore.dll"))
                    )
                {
                    return true;
                }
            }
            return false;
        }

        private List<String> missingEntries = new List<String>();

        private int AllEntriesStatus(InstallationTag tag, int updateVersion)
        {
            missingEntries.Clear();
            int status = 0;
            for (int i = 0; i < installXml.actions.Count; i++)
            {
                if (installXml.actions[i].mTag == tag && installXml.actions[i].mUpdateVersion == updateVersion && installXml.actions[i].mType == InstallationEntryType.UnpackArchive)
                {
                    status |= 1;
                    if (!System.IO.File.Exists(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, installXml.actions[i].mFileName)))
                    {
                        missingEntries.Add(installXml.actions[i].mFileName);
                        status |= 2;
                    }
                }
            }
            if (status == 3)
                return 1;
            else if (status == 1)
                return 2;
            return 0;
        }

        private List<UpdateInfo> GetInfoForFullInstall(int maxUpdate)
        {
            List<UpdateInfo> info = new List<UpdateInfo>();
            info.Add(new UpdateInfo());
            info[0].main = AllEntriesStatus(InstallationTag.GENERAL, 0);
            info[0].missingFiles.AddRange(missingEntries);
            info[0].portraits1 = AllEntriesStatus(InstallationTag.PORTRAITS1, 0);
            List<String> portraits1missing = new List<String>(missingEntries);
            info[0].portraits2 = AllEntriesStatus(InstallationTag.PORTRAITS2, 0);
            List<String> portraits2missing = new List<String>(missingEntries);
            bool checkPortraits1 = info[0].portraits1 == 2;
            bool checkPortraits2 = info[0].portraits2 == 2;
            if (info[0].portraits1 == 1 && info[0].portraits2 == 1)
            {
                info[0].missingFiles.AddRange(portraits1missing);
                info[0].missingFiles.AddRange(portraits2missing);
                checkPortraits1 = true;
                checkPortraits2 = true;
            }
            else if (info[0].portraits1 == 1 && info[0].portraits2 == 0)
            {
                info[0].missingFiles.AddRange(portraits1missing);
                checkPortraits1 = true;
            }
            else if (info[0].portraits1 == 0 && info[0].portraits2 == 1)
            {
                info[0].missingFiles.AddRange(portraits2missing);
                checkPortraits2 = true;
            }
            info[0].badges1 = AllEntriesStatus(InstallationTag.BADGES1, 0);
            List<String> badges1missing = new List<String>(missingEntries);
            info[0].badges2 = AllEntriesStatus(InstallationTag.BADGES2, 0);
            List<String> badges2missing = new List<String>(missingEntries);
            bool checkBadges1 = info[0].badges1 == 2;
            bool checkBadges2 = info[0].badges2 == 2;
            if (info[0].badges1 == 1 && info[0].badges2 == 1)
            {
                info[0].missingFiles.AddRange(badges1missing);
                info[0].missingFiles.AddRange(badges2missing);
                checkBadges1 = true;
                checkBadges2 = true;
            }
            else if (info[0].badges1 == 1 && info[0].badges2 == 0)
            {
                info[0].missingFiles.AddRange(badges1missing);
                checkBadges1 = true;
            }
            else if (info[0].badges1 == 0 && info[0].badges2 == 1)
            {
                info[0].missingFiles.AddRange(badges2missing);
                checkBadges2 = true;
            }
            info[0].kits = AllEntriesStatus(InstallationTag.KITS, 0);
            info[0].faces = AllEntriesStatus(InstallationTag.FACES, 0);
            info[0].banners = AllEntriesStatus(InstallationTag.BANNERS, 0);
            info[0].xxl_portraits = AllEntriesStatus(InstallationTag.XXL, 0);
            info[0].stadiums2 = AllEntriesStatus(InstallationTag.STADIUMS2, 0);
            info[0].stadiums1 = AllEntriesStatus(InstallationTag.STADIUMS1, 0);
            bool checkStadiums1 = info[0].stadiums1 == 2;
            bool checkStadiums2 = info[0].stadiums1 != 0 && info[0].stadiums2 == 2;
            if (info[0].stadiums1 == 1 && info[0].stadiums2 == 2) // Extended pack available, but Basic pack is not available
            {
                info[0].missingFiles.AddRange(missingEntries);
                checkStadiums1 = true;
                checkStadiums2 = true;
            }
            for (int u = 1; u <= maxUpdate; u++)
            {
                info.Add(new UpdateInfo());
                if (info[0].main != 0)
                {
                    info[u].main = AllEntriesStatus(InstallationTag.GENERAL, u);
                    info[u].missingFiles.AddRange(missingEntries);
                }
                if (checkPortraits1)
                {
                    info[u].portraits1 = AllEntriesStatus(InstallationTag.PORTRAITS1, u);
                    info[u].missingFiles.AddRange(missingEntries);
                }
                if (checkPortraits2)
                {
                    info[u].portraits2 = AllEntriesStatus(InstallationTag.PORTRAITS2, u);
                    info[u].missingFiles.AddRange(missingEntries);
                }
                if (checkBadges1)
                {
                    info[u].badges1 = AllEntriesStatus(InstallationTag.BADGES1, u);
                    info[u].missingFiles.AddRange(missingEntries);
                }
                if (checkBadges2)
                {
                    info[u].badges2 = AllEntriesStatus(InstallationTag.BADGES2, u);
                    info[u].missingFiles.AddRange(missingEntries);
                }
                if (info[0].kits == 2)
                {
                    info[u].kits = AllEntriesStatus(InstallationTag.KITS, u);
                    info[u].missingFiles.AddRange(missingEntries);
                }
                if (info[0].faces == 2)
                {
                    info[u].faces = AllEntriesStatus(InstallationTag.FACES, u);
                    info[u].missingFiles.AddRange(missingEntries);
                }
                if (info[0].banners == 2)
                {
                    info[u].banners = AllEntriesStatus(InstallationTag.BANNERS, u);
                    info[u].missingFiles.AddRange(missingEntries);
                }
                if (info[0].xxl_portraits == 2)
                {
                    info[u].xxl_portraits = AllEntriesStatus(InstallationTag.XXL, u);
                    info[u].missingFiles.AddRange(missingEntries);
                }
                if (checkStadiums1)
                {
                    info[u].stadiums1 = AllEntriesStatus(InstallationTag.STADIUMS1, u);
                    info[u].missingFiles.AddRange(missingEntries);
                }
                if (checkStadiums2)
                {
                    info[u].stadiums2 = AllEntriesStatus(InstallationTag.STADIUMS2, u);
                    info[u].missingFiles.AddRange(missingEntries);
                }
            }
            return info;
        }

        private List<UpdateInfo> GetInfoForUpdatesInstall(int minUpdate, int maxUpdate, InstallConfig config)
        {
            List<UpdateInfo> info = new List<UpdateInfo>();
            for (int i = 0; i <= maxUpdate - minUpdate; i++)
            {
                int u = i + minUpdate;
                info.Add(new UpdateInfo());
                info[i].main = AllEntriesStatus(InstallationTag.GENERAL, u);
                info[i].missingFiles.AddRange(missingEntries);
                if (config.portraits == 1)
                {
                    info[i].portraits1 = AllEntriesStatus(InstallationTag.PORTRAITS1, u);
                    info[i].missingFiles.AddRange(missingEntries);
                }
                if (config.portraits == 2)
                {
                    info[i].portraits2 = AllEntriesStatus(InstallationTag.PORTRAITS2, u);
                    info[i].missingFiles.AddRange(missingEntries);
                }
                if (config.badges == 1)
                {
                    info[i].badges1 = AllEntriesStatus(InstallationTag.BADGES1, u);
                    info[i].missingFiles.AddRange(missingEntries);
                }
                if (config.badges == 2)
                {
                    info[i].badges2 = AllEntriesStatus(InstallationTag.BADGES2, u);
                    info[i].missingFiles.AddRange(missingEntries);
                }
                if (config.kits)
                {
                    info[i].kits = AllEntriesStatus(InstallationTag.KITS, u);
                    info[i].missingFiles.AddRange(missingEntries);
                }
                if (config.faces)
                {
                    info[i].faces = AllEntriesStatus(InstallationTag.FACES, u);
                    info[i].missingFiles.AddRange(missingEntries);
                }
                if (config.banners)
                {
                    info[i].banners = AllEntriesStatus(InstallationTag.BANNERS, u);
                    info[i].missingFiles.AddRange(missingEntries);
                }
                if (config.xxl_portraits)
                {
                    info[i].xxl_portraits = AllEntriesStatus(InstallationTag.XXL, u);
                    info[i].missingFiles.AddRange(missingEntries);
                }
                if (config.stadiums == 1 || config.stadiums == 2)
                {
                    info[i].stadiums1 = AllEntriesStatus(InstallationTag.STADIUMS1, u);
                    info[i].missingFiles.AddRange(missingEntries);
                    if (config.stadiums == 2)
                    {
                        info[i].stadiums2 = AllEntriesStatus(InstallationTag.STADIUMS2, u);
                        info[i].missingFiles.AddRange(missingEntries);
                    }
                }
            }
            return info;
        }

        private String GetErrors(List<UpdateInfo> info, int minUpdate)
        {
            String errors = "";
            for (int i = 0; i < info.Count; i++)
            {
                if (info[i].missingFiles.Count > 0)
                {
                    if (i + minUpdate > 0)
                    {
                        errors += Bold(String.Format(languages.Resource.INSTALLDIR_UPDATE, i + minUpdate)) + NewLine;
                    }
                    for (int f = 0; f < info[i].missingFiles.Count; f++)
                    {
                        errors += info[i].missingFiles[f];
                        errors += NewLine;
                    }

                }
            }
            return errors;
        }

        public void SetRTFText(string text)
        {
            var sb = new StringBuilder();
            foreach (var c in text)
            {
                if (c == '\\' || c == '{' || c == '}')
                    sb.Append(@"\" + c);
                else if (c <= 0x7f)
                    sb.Append(c);
                else
                    sb.Append("\\u" + Convert.ToUInt32(c) + "?");
            }
            MemoryStream stream = new MemoryStream(ASCIIEncoding.Default.GetBytes("{\\rtf1\\fs20\\deff0 {\\colortbl;\\red0\\green0\\blue0;\\red0\\green255\\blue0;\\red255\\green0\\blue0;}" + sb.ToString() + "}"));
            TxNote.Document.Blocks.Clear();
            TxNote.Selection.Load(stream, DataFormats.Rtf);
        }

        private String OptionUpdates(int min, int max, bool reinstall)
        {
            if (max == min)
            {
                if (reinstall)
                {
                    return String.Format(languages.Resource.INSTALLDIR_REINSTALL_UPDATE, max);
                }
                else
                {
                    return String.Format(languages.Resource.INSTALLDIR_INSTALL_UPDATE, max);
                }
            }
            else
            {
                String updatesList = String.Format("{0}", min);
                for (int i = min + 1; i < max; i++)
                {
                    updatesList += String.Format(", {0}", i);
                }
                if (reinstall)
                {
                    return String.Format(languages.Resource.INSTALLDIR_REINSTALL_UPDATES, updatesList, max);
                }
                else
                {
                    return String.Format(languages.Resource.INSTALLDIR_INSTALL_UPDATES, updatesList, max);
                }
            }
        }

        private void CheckInstallationDir()
        {
            installFiles = new InstallFiles();
            mMinUpdateOption = 0;
            if (IsCorrectGameFolder(TbInstallDir.Text))
            {
                BtnNext.IsEnabled = true;
                TxInstallDirStatus.Text = "";
                string iniFileName = System.IO.Path.Combine(TbInstallDir.Text, "installer.ini");
                installConfig.installed = false;
                if (System.IO.File.Exists(iniFileName))
                {
                    if (GetPrivateProfileInt("MAIN", "Installed", 0, iniFileName) == 1)
                    {
                        StringBuilder sb = new StringBuilder(256);
                        GetPrivateProfileString("MAIN", "Version", "", sb, sb.Capacity, iniFileName);
                        installConfig.version = sb.ToString();
                        var versionParts = installConfig.version.Split('.');
                        if (versionParts.Count() == 2)
                        {
                            installConfig.versionYear = versionParts[0];
                            installConfig.versionUpdate = ParseInt(versionParts[1]);
                        }
                        if (installConfig.versionYear == installXml.version)
                        {
                            installConfig.portraits = GetPrivateProfileInt("MAIN", "Portraits", 0, iniFileName);
                            installConfig.badges = GetPrivateProfileInt("MAIN", "Badges", 0, iniFileName);
                            installConfig.kits = GetPrivateProfileInt("MAIN", "Kits", 0, iniFileName) == 1;
                            installConfig.faces = GetPrivateProfileInt("MAIN", "Faces", 0, iniFileName) == 1;
                            installConfig.banners = GetPrivateProfileInt("MAIN", "Banners", 0, iniFileName) == 1;
                            installConfig.xxl_portraits = GetPrivateProfileInt("MAIN", "XxlPortraits", 0, iniFileName) == 1;
                            installConfig.stadiums = GetPrivateProfileInt("MAIN", "Stadiums", 0, iniFileName);
                            installConfig.installed = true;
                        }
                    }
                }
                int lastUpdate = installXml.update;
                int currUpdate = installConfig.versionUpdate;
                String message = "";
                String option1 = "";
                String option2 = "";
                List<UpdateInfo> fullInstallInfo = GetInfoForFullInstall(installXml.update);
                String errorsFullInstall = GetErrors(fullInstallInfo, 0);
                if (installConfig.installed)
                {
                    String optionReinstall = lastUpdate != 0 ? languages.Resource.INSTALLDIR_REINSTALL_WITH_UPDATES : languages.Resource.INSTALLDIR_REINSTALL;
                    if (currUpdate < lastUpdate)
                    {
                        message = String.Format(languages.Resource.INSTALLDIR_VERSION_CURRENT, installXml.version, currUpdate);
                        List<UpdateInfo> updatesInfo = GetInfoForUpdatesInstall(currUpdate + 1, lastUpdate, installConfig);
                        String errorsUpdates = GetErrors(updatesInfo, currUpdate + 1);
                        if (errorsUpdates == "") // can install updates
                        {
                            option2 = OptionUpdates(currUpdate + 1, lastUpdate, false);
                            mMinUpdateOption = currUpdate + 1;
                            if (errorsFullInstall == "") // can install full
                            {
                                option1 = optionReinstall;
                            }
                        }
                        else // can't install updates
                        {
                            if (errorsFullInstall == "") // still can install full
                            {
                                option1 = optionReinstall;
                                message += NewLine + Bold(languages.Resource.INSTALLDIR_CONFIG_DOES_NOT_MATCH) + NewLine + languages.Resource.INSTALLDIR_CONFIG_MAKE_SURE + NewLine + errorsUpdates;
                            }
                            else // can't install full
                            {
                                message += NewLine + Bold(Red(languages.Resource.INSTALLDIR_MISSING_FILES_UPDATES)) + NewLine + errorsUpdates;
                            }
                        }
                    }
                    else if (currUpdate > lastUpdate)
                    {
                        message = String.Format(languages.Resource.INSTALLDIR_VERSION_GREATER, installXml.version, currUpdate, lastUpdate);
                        if (errorsFullInstall == "")
                        {
                            option1 = optionReinstall;
                        }
                        else
                        {
                            message += NewLine + Bold(Red(languages.Resource.INSTALLDIR_MISSING_FILES_REINSTALL_BUT)) + NewLine + errorsFullInstall;
                        }
                    }
                    else // currUpdate == lastUpdate
                    {
                        message = String.Format(languages.Resource.INSTALLDIR_VERSION_SAME, installXml.version, currUpdate);
                        if (errorsFullInstall == "") // can install full
                        {
                            option1 = optionReinstall;
                            message += " " + languages.Resource.INSTALLDIR_STILL_REINSTALL;
                        }
                        else // can't install full
                        {
                            int minPossibleUpdate = 0;
                            if (lastUpdate != 0)
                            {
                                for (int i = lastUpdate; i >= 1; i--)
                                {
                                    List<UpdateInfo> updatesInfo = GetInfoForUpdatesInstall(i, i, installConfig);
                                    String errorsUpdates = GetErrors(updatesInfo, i);
                                    if (errorsUpdates == "") // can install update
                                    {
                                        minPossibleUpdate = i;
                                    }
                                }
                            }
                            if (minPossibleUpdate == 0)
                            {
                                message += NewLine + Bold(languages.Resource.INSTALLDIR_MISSING_FILES_REINSTALL) + NewLine + errorsFullInstall;
                            }
                            else
                            {
                                option2 = OptionUpdates(minPossibleUpdate, lastUpdate, true);
                                mMinUpdateOption = minPossibleUpdate;
                                if (minPossibleUpdate == lastUpdate)
                                {
                                    message += " " + String.Format(languages.Resource.INSTALLDIR_STILL_REINSTALL_UPDATE, minPossibleUpdate);
                                }
                                else
                                {
                                    String updatesList = String.Format("{0}", minPossibleUpdate);
                                    for (int i = minPossibleUpdate + 1; i < lastUpdate; i++)
                                    {
                                        updatesList += String.Format(", {0}", i);
                                    }
                                    message += " " + String.Format(languages.Resource.INSTALLDIR_STILL_REINSTALL_UPDATES, updatesList, lastUpdate);
                                }
                            }
                        }
                    }
                }
                else
                {
                    message = String.Format(languages.Resource.INSTALLDIR_VERSION_NOT_INSTALLED, installXml.version);
                    if (errorsFullInstall == "")
                    {
                        option1 = lastUpdate != 0 ? languages.Resource.INSTALLDIR_INSTALL_WITH_UPDATES : languages.Resource.INSTALLDIR_INSTALL;
                    }
                    else
                    {
                        message += NewLine + Bold(Red(languages.Resource.INSTALLDIR_MISSING_FILES)) + NewLine + errorsFullInstall;
                    }
                }
                SetRTFText(message);
                if (option1 != "" || option2 != "")
                {
                    ((ComboBoxItem)CbInstallType.Items[0]).Content = option1;
                    ((ComboBoxItem)CbInstallType.Items[1]).Content = option2;
                    CbInstallType.SelectedIndex = option2 != "" ? 1 : 0;
                    CbInstallType.IsEnabled = option1 != "" && option2 != "";
                    TxInstallNext.Visibility = Visibility.Visible;
                    BtnNext.IsEnabled = true;
                }
                else
                {
                    CbInstallType.SelectedIndex = -1;
                    CbInstallType.IsEnabled = false;
                    TxInstallNext.Visibility = Visibility.Hidden;
                    BtnNext.IsEnabled = false;
                }
                GridInstallType.Visibility = Visibility.Visible;
                if (errorsFullInstall == "" && fullInstallInfo.Count > 0)
                {
                    installFiles.main = true;
                    installFiles.xxl_portraits = fullInstallInfo[0].xxl_portraits == 2;
                    installFiles.kits = fullInstallInfo[0].kits == 2;
                    installFiles.faces = fullInstallInfo[0].faces == 2;
                    installFiles.banners = fullInstallInfo[0].banners == 2;
                    installFiles.stadiums = 0;
                    if (fullInstallInfo[0].stadiums1 == 2)
                    {
                        if (fullInstallInfo[0].stadiums2 == 2)
                        {
                            installFiles.stadiums = 2;
                        }
                        else
                        {
                            installFiles.stadiums = 1;
                        }
                    }
                    installFiles.portraits = 0;
                    if (fullInstallInfo[0].portraits1 == 2)
                        installFiles.portraits |= 1;
                    if (fullInstallInfo[0].portraits2 == 2)
                        installFiles.portraits |= 2;
                    installFiles.badges = 0;
                    if (fullInstallInfo[0].badges1 == 2)
                        installFiles.badges |= 1;
                    if (fullInstallInfo[0].badges2 == 2)
                        installFiles.badges |= 2;
                }
            }
            else
            {
                GridInstallType.Visibility = Visibility.Hidden;
                BtnOk.IsEnabled = false;
                if (TbInstallDir.Text == "")
                {
                    TxInstallDirStatus.Text = "";
                }
                else
                {
                    TxInstallDirStatus.Text = languages.Resource.INSTALLDIR_ERROR;
                }
            }
        }

        private void BtnBrowseGameDir_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;
            CommonFileDialogResult result = dialog.ShowDialog();
            if (result == CommonFileDialogResult.Ok)
            {
                TbInstallDir.Text = dialog.FileName;
                CheckInstallationDir();
            }
        }

        private void TbInstallDir_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!initialized)
                return;
            CheckInstallationDir();
        }
    }
}
