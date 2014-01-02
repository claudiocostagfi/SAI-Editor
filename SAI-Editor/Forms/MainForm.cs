﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MySql.Data.MySqlClient;
using SAI_Editor.Classes;
using SAI_Editor.Classes.Database.Classes;
using SAI_Editor.Enumerators;
using SAI_Editor.Forms.SearchForms;
using SAI_Editor.Properties;
using System.IO;
using System.Reflection;

namespace SAI_Editor.Forms
{
    public enum FormState
    {
        FormStateLogin,
        FormStateExpandingOrContracting,
        FormStateMain,
    }

    internal enum FormSizes
    {
        LoginFormWidth = 403,
        LoginFormHeight = 236,

        MainFormWidth = 957,
        MainFormHeight = 505,

        ListViewHeightContract = 65,

        LoginFormHeightShowWarning = 309,
    }

    internal enum MaxValues
    {
        MaxEventType = 74,
        MaxActionType = 110,
        MaxTargetType = 26,
    }

    public enum SourceTypes
    {
        SourceTypeNone = -1,
        SourceTypeCreature = 0,
        SourceTypeGameobject = 1,
        SourceTypeAreaTrigger = 2,
        SourceTypeScriptedActionlist = 9,
    }

    public struct EntryOrGuidAndSourceType
    {
        public EntryOrGuidAndSourceType(int _entryOrGuid, SourceTypes _sourceType) { entryOrGuid = _entryOrGuid; sourceType = _sourceType; }

        public int entryOrGuid;
        public SourceTypes sourceType;
    }

    public partial class MainForm : Form
    {
        public MySqlConnectionStringBuilder connectionString = new MySqlConnectionStringBuilder();
        private readonly List<Control> controlsLoginForm = new List<Control>();
        private readonly List<Control> controlsMainForm = new List<Control>();
        private bool contractingToLoginForm, expandingToMainForm, expandingListView, contractingListView;
        private int originalHeight = 0, originalWidth = 0;
        private int MainFormWidth = (int)FormSizes.MainFormWidth, MainFormHeight = (int)FormSizes.MainFormHeight;
        private int listViewSmartScriptsInitialHeight, listViewSmartScriptsHeightToChangeTo;
        public int expandAndContractSpeed = 5, expandAndContractSpeedListView = 2;
        public EntryOrGuidAndSourceType originalEntryOrGuidAndSourceType = new EntryOrGuidAndSourceType();
        public int lastSmartScriptIdOfScript = 0;
        private readonly ListViewColumnSorter lvwColumnSorter = new ListViewColumnSorter();
        private bool runningConstructor = false, updatingFieldsBasedOnSelectedScript = false;
        private int previousLinkFrom = -1;
        private List<SmartScript> lastDeletedSmartScripts = new List<SmartScript>(), smartScriptsOnClipBoard = new List<SmartScript>();
        private readonly string applicationVersion = String.Empty;

        private FormState _formState = FormState.FormStateLogin;
        public FormState formState
        {
            get
            {
                return _formState;
            }
            set
            {
                _formState = value;

                switch (_formState)
                {
                    case FormState.FormStateExpandingOrContracting:
                        FormBorderStyle = FormBorderStyle.FixedDialog; //! Don't allow resizing by user
                        MainFormHeight = Settings.Default.MainFormHeight;
                        MinimumSize = new Size((int)FormSizes.LoginFormWidth, (int)FormSizes.LoginFormHeight);
                        MaximumSize = new Size(MainFormWidth, MainFormHeight);
                        panelPermanentTooltipTypes.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                        panelPermanentTooltipParameters.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                        break;
                    case FormState.FormStateLogin:
                        FormBorderStyle = FormBorderStyle.FixedDialog;
                        MinimumSize = new Size((int)FormSizes.LoginFormWidth, (int)FormSizes.LoginFormHeight);
                        MaximumSize = new Size((int)FormSizes.LoginFormWidth, (int)FormSizes.LoginFormHeight);
                        panelPermanentTooltipTypes.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                        panelPermanentTooltipParameters.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                        break;
                    case FormState.FormStateMain:
                        //FormBorderStyle = FormBorderStyle.Sizable;
                        MinimumSize = new Size(MainFormWidth, (int)FormSizes.MainFormHeight);
                        MaximumSize = new Size(MainFormWidth, (int)FormSizes.MainFormHeight + 100);
                        panelPermanentTooltipTypes.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                        panelPermanentTooltipParameters.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                        break;
                }
            }
        }

        public MainForm()
        {
            InitializeComponent();

            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            applicationVersion = "v" + version.Major + "." + version.Minor;
            Text = "SAI-Editor " + applicationVersion + ": Login";
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            runningConstructor = true;
            menuStrip.Visible = false; //! Doing this in main code so we can actually see the menustrip in designform

            Width = (int)FormSizes.LoginFormWidth;
            Height = (int)FormSizes.LoginFormHeight;

            originalHeight = Height;
            originalWidth = Width;

            if (MainFormWidth > SystemInformation.VirtualScreen.Width)
                MainFormWidth = SystemInformation.VirtualScreen.Width;

            if (MainFormHeight > SystemInformation.VirtualScreen.Height)
                MainFormHeight = SystemInformation.VirtualScreen.Height;

            try
            {
                textBoxHost.Text = Settings.Default.Host;
                textBoxUsername.Text = Settings.Default.User;
                textBoxPassword.Text = SAI_Editor_Manager.Instance.GetPasswordSetting();
                textBoxWorldDatabase.Text = Settings.Default.Database;
                textBoxPort.Text = Settings.Default.Port > 0 ? Settings.Default.Port.ToString() : String.Empty;
                expandAndContractSpeed = Settings.Default.AnimationSpeed;
                radioButtonConnectToMySql.Checked = Settings.Default.UseWorldDatabase;
                radioButtonDontUseDatabase.Checked = !Settings.Default.UseWorldDatabase;
                checkBoxListActionlistsOrEntries.Enabled = Settings.Default.UseWorldDatabase;
                menuItemRevertQuery.Enabled = Settings.Default.UseWorldDatabase;
                SetGenerateCommentsEnabled(listViewSmartScripts.Items.Count > 0 && Settings.Default.UseWorldDatabase);
                buttonSearchForEntryOrGuid.Enabled = Settings.Default.UseWorldDatabase || (SourceTypes)Settings.Default.LastSourceType == SourceTypes.SourceTypeAreaTrigger;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            foreach (Control control in Controls)
            {
                //! These two are set manually because otherwise they will always show for a split second before disappearing again.
                if (control.Name == "panelPermanentTooltipTypes" || control.Name == "panelPermanentTooltipParameters")
                    continue;

                if (control.Visible)
                    controlsLoginForm.Add(control);
                else
                    controlsMainForm.Add(control);
            }

            comboBoxSourceType.SelectedIndex = 0;
            comboBoxEventType.SelectedIndex = 0;
            comboBoxActionType.SelectedIndex = 0;
            comboBoxTargetType.SelectedIndex = 0;

            //! We first load the information and then change the parameter fields
            await SAI_Editor_Manager.Instance.LoadSQLiteDatabaseInfo();
            ChangeParameterFieldsBasedOnType();

            if (Settings.Default.AutoConnect)
            {
                checkBoxAutoConnect.Checked = true;

                if (Settings.Default.UseWorldDatabase)
                {
                    connectionString = new MySqlConnectionStringBuilder();
                    connectionString.Server = textBoxHost.Text;
                    connectionString.UserID = textBoxUsername.Text;
                    connectionString.Port = XConverter.ToUInt32(textBoxPort.Text);
                    connectionString.Database = textBoxWorldDatabase.Text;

                    if (textBoxPassword.Text.Length > 0)
                        connectionString.Password = textBoxPassword.Text;
                }

                if (!Settings.Default.UseWorldDatabase || SAI_Editor_Manager.Instance.worldDatabase.CanConnectToDatabase(connectionString, false))
                {
                    SAI_Editor_Manager.Instance.ResetWorldDatabase(connectionString);
                    buttonConnect.PerformClick();

                    if (Settings.Default.InstantExpand)
                        StartExpandingToMainForm(true);
                }
            }

            tabControlParameters.AutoScrollOffset = new Point(5, 5);

            //! Permanent scrollbar to the parameters tabpage windows
            foreach (TabPage page in tabControlParameters.TabPages)
            {
                page.HorizontalScroll.Enabled = false;
                page.HorizontalScroll.Visible = false;

                page.AutoScroll = true;
                page.AutoScrollMinSize = new Size(page.Width, page.Height);
            }

            panelLoginBox.Location = new Point(9, 8);

            if (Settings.Default.HidePass)
                textBoxPassword.PasswordChar = '●';

            textBoxComments.GotFocus += textBoxComments_GotFocus;
            textBoxComments.LostFocus += textBoxComments_LostFocus;

            panelPermanentTooltipTypes.BackColor = Color.FromArgb(255, 255, 225);
            panelPermanentTooltipParameters.BackColor = Color.FromArgb(255, 255, 225);
            labelPermanentTooltipTextTypes.BackColor = Color.FromArgb(255, 255, 225);

            pictureBoxLoadScript.Enabled = textBoxEntryOrGuid.Text.Length > 0 && Settings.Default.UseWorldDatabase;
            pictureBoxCreateScript.Enabled = textBoxEntryOrGuid.Text.Length > 0;

            textBoxEventType.MouseWheel += textBoxEventType_MouseWheel;
            textBoxActionType.MouseWheel += textBoxActionType_MouseWheel;
            textBoxTargetType.MouseWheel += textBoxTargetType_MouseWheel;

            buttonNewLine.Enabled = textBoxEntryOrGuid.Text.Length > 0;

            runningConstructor = false;
        }

        private void timerExpandOrContract_Tick(object sender, EventArgs e)
        {
            if (expandingToMainForm)
            {
                if (Height < MainFormHeight)
                    Height += expandAndContractSpeed;
                else
                {
                    Height = MainFormHeight;

                    if (Width >= MainFormWidth && timerExpandOrContract.Enabled) //! If both finished
                    {
                        Width = MainFormWidth;
                        timerExpandOrContract.Enabled = false;
                        expandingToMainForm = false;
                        formState = FormState.FormStateMain;
                        FinishedExpandingOrContracting(true);
                    }
                }

                if (Width < MainFormWidth)
                    Width += expandAndContractSpeed;
                else
                {
                    Width = MainFormWidth;

                    if (Height >= MainFormHeight && timerExpandOrContract.Enabled) //! If both finished
                    {
                        Height = MainFormHeight;
                        timerExpandOrContract.Enabled = false;
                        expandingToMainForm = false;
                        formState = FormState.FormStateMain;
                        FinishedExpandingOrContracting(true);
                    }
                }
            }
            else if (contractingToLoginForm)
            {
                if (Height > originalHeight)
                    Height -= expandAndContractSpeed;
                else
                {
                    Height = originalHeight;

                    if (Width <= originalWidth && timerExpandOrContract.Enabled) //! If both finished
                    {
                        Width = originalWidth;
                        timerExpandOrContract.Enabled = false;
                        contractingToLoginForm = false;
                        formState = FormState.FormStateLogin;
                        FinishedExpandingOrContracting(false);
                    }
                }

                if (Width > originalWidth)
                    Width -= expandAndContractSpeed;
                else
                {
                    Width = originalWidth;

                    if (Height <= originalHeight && timerExpandOrContract.Enabled) //! If both finished
                    {
                        Height = originalHeight;
                        timerExpandOrContract.Enabled = false;
                        contractingToLoginForm = false;
                        formState = FormState.FormStateLogin;
                        FinishedExpandingOrContracting(false);
                    }
                }
            }
        }

        private void buttonConnect_Click(object sender, EventArgs e)
        {
            bool connectToMySql = radioButtonConnectToMySql.Checked;

            if (connectToMySql)
            {
                if (String.IsNullOrEmpty(textBoxHost.Text))
                {
                    MessageBox.Show("The host field has to be filled!", "An error has occurred!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (String.IsNullOrEmpty(textBoxUsername.Text))
                {
                    MessageBox.Show("The username field has to be filled!", "An error has occurred!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (textBoxPassword.Text.Length > 0 && String.IsNullOrEmpty(textBoxPassword.Text))
                {
                    MessageBox.Show("The password field can not consist of only whitespaces!", "An error has occurred!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (String.IsNullOrEmpty(textBoxWorldDatabase.Text))
                {
                    MessageBox.Show("The world database field has to be filled!", "An error has occurred!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (String.IsNullOrEmpty(textBoxPort.Text))
                {
                    MessageBox.Show("The port field has to be filled!", "An error has occurred!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                connectionString = new MySqlConnectionStringBuilder();
                connectionString.Server = textBoxHost.Text;
                connectionString.UserID = textBoxUsername.Text;
                connectionString.Port = XConverter.ToUInt32(textBoxPort.Text);
                connectionString.Database = textBoxWorldDatabase.Text;

                if (textBoxPassword.Text.Length > 0)
                    connectionString.Password = textBoxPassword.Text;
            }

            Settings.Default.UseWorldDatabase = connectToMySql;
            Settings.Default.Save();

            if (!connectToMySql || SAI_Editor_Manager.Instance.worldDatabase.CanConnectToDatabase(connectionString))
            {
                StartExpandingToMainForm(Settings.Default.InstantExpand);

                if (!connectToMySql)
                    SAI_Editor_Manager.Instance.ResetDatabases();

                HandleUseWorldDatabaseSettingChanged();
            }
        }

        private void StartExpandingToMainForm(bool instant = false)
        {
            if (radioButtonConnectToMySql.Checked)
            {
                RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
                byte[] buffer = new byte[1024];
                rng.GetBytes(buffer);
                string salt = BitConverter.ToString(buffer);
                rng.Dispose();

                Settings.Default.Entropy = salt;
                Settings.Default.Host = textBoxHost.Text;
                Settings.Default.User = textBoxUsername.Text;
                Settings.Default.Password = textBoxPassword.Text.Length == 0 ? String.Empty : textBoxPassword.Text.ToSecureString().EncryptString(Encoding.Unicode.GetBytes(salt));
                Settings.Default.Database = textBoxWorldDatabase.Text;
                Settings.Default.AutoConnect = checkBoxAutoConnect.Checked;
                Settings.Default.Port = XConverter.ToUInt32(textBoxPort.Text);
                Settings.Default.UseWorldDatabase = true;
                Settings.Default.Save();
            }

            ResetFieldsToDefault();

            if (radioButtonConnectToMySql.Checked)
                Text = "SAI-Editor " + applicationVersion + " - Connection: " + textBoxUsername.Text + ", " + textBoxHost.Text + ", " + textBoxPort.Text;
            else
                Text = "SAI-Editor " + applicationVersion + " - Creator-only mode, no database connection";

            if (instant)
            {
                Width = MainFormWidth;
                Height = Settings.Default.MainFormHeight;
                formState = FormState.FormStateMain;
                FinishedExpandingOrContracting(true);
            }
            else
            {
                formState = FormState.FormStateExpandingOrContracting;
                timerExpandOrContract.Enabled = true;
                expandingToMainForm = true;
            }

            foreach (Control control in controlsLoginForm)
                control.Visible = false;

            foreach (Control control in controlsMainForm)
                control.Visible = instant;

            panelPermanentTooltipTypes.Visible = false;
            panelPermanentTooltipParameters.Visible = false;
        }

        private void StartContractingToLoginForm(bool instant = false)
        {
            Text = "SAI-Editor " + applicationVersion + ": Login";

            if (Settings.Default.ShowTooltipsPermanently)
                listViewSmartScripts.Height += (int)FormSizes.ListViewHeightContract;

            if (instant)
            {
                Width = originalWidth;
                Height = originalHeight;
                formState = FormState.FormStateLogin;
                FinishedExpandingOrContracting(false);
            }
            else
            {
                formState = FormState.FormStateExpandingOrContracting;
                timerExpandOrContract.Enabled = true;
                contractingToLoginForm = true;
            }

            foreach (var control in controlsLoginForm)
                control.Visible = instant;

            foreach (var control in controlsMainForm)
                control.Visible = false;
        }

        private void buttonClear_Click(object sender, EventArgs e)
        {
            textBoxHost.Text = "";
            textBoxUsername.Text = "";
            textBoxPassword.Text = "";
            textBoxWorldDatabase.Text = "";
            textBoxPort.Text = "";
            checkBoxAutoConnect.Checked = false;
            radioButtonConnectToMySql.Checked = true;
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    switch (formState)
                    {
                        case FormState.FormStateLogin:
                            buttonConnect.PerformClick();
                            break;
                        case FormState.FormStateMain:
                            if (textBoxEntryOrGuid.Focused)
                            {
                                if (Settings.Default.UseWorldDatabase)
                                    pictureBoxLoadScript_Click(pictureBoxLoadScript, null);
                                else
                                    pictureBoxCreateScript_Click(pictureBoxCreateScript, null);
                            }

                            break;
                    }
                    break;
            }
        }

        private void buttonSearchForEntry_Click(object sender, EventArgs e)
        {
            //! Just keep it in main thread; no purpose starting a new thread for this (unless workspaces get implemented, maybe)
            using (var entryForm = new SearchForEntryForm(connectionString, textBoxEntryOrGuid.Text, GetSourceTypeByIndex()))
                entryForm.ShowDialog(this);
        }

        private void menuItemReconnect_Click(object sender, EventArgs e)
        {
            if (formState != FormState.FormStateMain)
                return;

            panelPermanentTooltipTypes.Visible = false;
            panelPermanentTooltipParameters.Visible = false;
            SaveLastUsedFields();
            ResetFieldsToDefault();
            listViewSmartScripts.ReplaceSmartScripts(new List<SmartScript>());
            StartContractingToLoginForm(Settings.Default.InstantExpand);
        }

        private async void comboBoxEventType_SelectedIndexChanged(object sender, EventArgs e)
        {
            textBoxEventType.Text = comboBoxEventType.SelectedIndex.ToString();
            textBoxEventType.SelectionStart = 3; //! Set cursor to end of text

            if (!runningConstructor)
            {
                ChangeParameterFieldsBasedOnType();
                UpdatePermanentTooltipOfTypes(comboBoxEventType, ScriptTypeId.ScriptTypeEvent);
            }

            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.event_type = comboBoxEventType.SelectedIndex;
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                //listViewSmartScripts.SelectedItems[0].SubItems[4].Text = comboBoxEventType.SelectedIndex.ToString();
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void comboBoxActionType_SelectedIndexChanged(object sender, EventArgs e)
        {
            textBoxActionType.Text = comboBoxActionType.SelectedIndex.ToString();
            textBoxActionType.SelectionStart = 3; //! Set cursor to end of text

            if (!runningConstructor)
            {
                ChangeParameterFieldsBasedOnType();
                UpdatePermanentTooltipOfTypes(comboBoxActionType, ScriptTypeId.ScriptTypeAction);
            }

            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.action_type = comboBoxActionType.SelectedIndex;
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                //listViewSmartScripts.SelectedItems[0].SubItems[12].Text = comboBoxActionType.SelectedIndex.ToString();
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void comboBoxTargetType_SelectedIndexChanged(object sender, EventArgs e)
        {
            textBoxTargetType.Text = comboBoxTargetType.SelectedIndex.ToString();
            textBoxTargetType.SelectionStart = 3; //! Set cursor to end of text

            if (!runningConstructor)
            {
                ChangeParameterFieldsBasedOnType();
                UpdatePermanentTooltipOfTypes(comboBoxTargetType, ScriptTypeId.ScriptTypeTarget);
            }

            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.target_type = comboBoxTargetType.SelectedIndex;
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                //listViewSmartScripts.SelectedItems[0].SubItems[19].Text = comboBoxTargetType.SelectedIndex.ToString();
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private void ChangeParameterFieldsBasedOnType()
        {
            //! Event parameters
            int event_type = comboBoxEventType.SelectedIndex;
            labelEventParam1.Text = SAI_Editor_Manager.Instance.GetParameterStringById(event_type, 1, ScriptTypeId.ScriptTypeEvent);
            labelEventParam2.Text = SAI_Editor_Manager.Instance.GetParameterStringById(event_type, 2, ScriptTypeId.ScriptTypeEvent);
            labelEventParam3.Text = SAI_Editor_Manager.Instance.GetParameterStringById(event_type, 3, ScriptTypeId.ScriptTypeEvent);
            labelEventParam4.Text = SAI_Editor_Manager.Instance.GetParameterStringById(event_type, 4, ScriptTypeId.ScriptTypeEvent);

            if (!Settings.Default.ShowTooltipsPermanently)
            {
                AddTooltip(comboBoxEventType, comboBoxEventType.SelectedItem.ToString(), SAI_Editor_Manager.Instance.GetScriptTypeTooltipById(event_type, ScriptTypeId.ScriptTypeEvent));
                AddTooltip(labelEventParam1, labelEventParam1.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(event_type, 1, ScriptTypeId.ScriptTypeEvent));
                AddTooltip(labelEventParam2, labelEventParam2.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(event_type, 2, ScriptTypeId.ScriptTypeEvent));
                AddTooltip(labelEventParam3, labelEventParam3.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(event_type, 3, ScriptTypeId.ScriptTypeEvent));
                AddTooltip(labelEventParam4, labelEventParam4.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(event_type, 4, ScriptTypeId.ScriptTypeEvent));
            }

            //! Action parameters
            int action_type = comboBoxActionType.SelectedIndex;
            labelActionParam1.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 1, ScriptTypeId.ScriptTypeAction);
            labelActionParam2.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 2, ScriptTypeId.ScriptTypeAction);
            labelActionParam3.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 3, ScriptTypeId.ScriptTypeAction);
            labelActionParam4.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 4, ScriptTypeId.ScriptTypeAction);
            labelActionParam5.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 5, ScriptTypeId.ScriptTypeAction);
            labelActionParam6.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 6, ScriptTypeId.ScriptTypeAction);

            if (!Settings.Default.ShowTooltipsPermanently)
            {
                AddTooltip(comboBoxActionType, comboBoxActionType.SelectedItem.ToString(), SAI_Editor_Manager.Instance.GetScriptTypeTooltipById(action_type, ScriptTypeId.ScriptTypeAction));
                AddTooltip(labelActionParam1, labelActionParam1.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 1, ScriptTypeId.ScriptTypeAction));
                AddTooltip(labelActionParam2, labelActionParam2.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 2, ScriptTypeId.ScriptTypeAction));
                AddTooltip(labelActionParam3, labelActionParam3.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 3, ScriptTypeId.ScriptTypeAction));
                AddTooltip(labelActionParam4, labelActionParam4.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 4, ScriptTypeId.ScriptTypeAction));
                AddTooltip(labelActionParam5, labelActionParam5.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 5, ScriptTypeId.ScriptTypeAction));
                AddTooltip(labelActionParam6, labelActionParam6.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 6, ScriptTypeId.ScriptTypeAction));
            }

            //! Target parameters
            int target_type = comboBoxTargetType.SelectedIndex;
            labelTargetParam1.Text = SAI_Editor_Manager.Instance.GetParameterStringById(target_type, 1, ScriptTypeId.ScriptTypeTarget);
            labelTargetParam2.Text = SAI_Editor_Manager.Instance.GetParameterStringById(target_type, 2, ScriptTypeId.ScriptTypeTarget);
            labelTargetParam3.Text = SAI_Editor_Manager.Instance.GetParameterStringById(target_type, 3, ScriptTypeId.ScriptTypeTarget);

            if (!Settings.Default.ShowTooltipsPermanently)
            {
                AddTooltip(comboBoxTargetType, comboBoxTargetType.SelectedItem.ToString(), SAI_Editor_Manager.Instance.GetScriptTypeTooltipById(target_type, ScriptTypeId.ScriptTypeTarget));
                AddTooltip(labelTargetParam1, labelTargetParam1.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(target_type, 1, ScriptTypeId.ScriptTypeTarget));
                AddTooltip(labelTargetParam2, labelTargetParam2.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(target_type, 2, ScriptTypeId.ScriptTypeTarget));
                AddTooltip(labelTargetParam3, labelTargetParam3.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(target_type, 3, ScriptTypeId.ScriptTypeTarget));
            }

            AdjustAllParameterFields(event_type, action_type, target_type);
        }

        private void checkBoxLockEventId_CheckedChanged(object sender, EventArgs e)
        {
            textBoxId.Enabled = !checkBoxLockEventId.Checked;
        }

        private void FinishedExpandingOrContracting(bool expanding)
        {
            foreach (var control in controlsLoginForm)
                control.Visible = !expanding;

            foreach (var control in controlsMainForm)
                control.Visible = expanding;

            if (!expanding)
                HandleHeightLoginFormBasedOnuseDatabaseSetting();

            panelPermanentTooltipTypes.Visible = false;
            panelPermanentTooltipParameters.Visible = false;

            textBoxEntryOrGuid.Text = Settings.Default.LastEntryOrGuid;
            comboBoxSourceType.SelectedIndex = Settings.Default.LastSourceType;
            checkBoxShowBasicInfo.Checked = Settings.Default.ShowBasicInfo;
            checkBoxLockEventId.Checked = Settings.Default.LockSmartScriptId;
            checkBoxListActionlistsOrEntries.Checked = Settings.Default.ListActionLists;
            checkBoxAllowChangingEntryAndSourceType.Checked = Settings.Default.AllowChangingEntryAndSourceType;
            checkBoxUsePhaseColors.Checked = Settings.Default.PhaseHighlighting;
            checkBoxUsePermanentTooltips.Checked = Settings.Default.ShowTooltipsPermanently;

            if (expanding && radioButtonConnectToMySql.Checked)
                TryToLoadScript(showErrorIfNoneFound: false);
        }

        private async Task<List<SmartScript>> GetSmartScriptsForEntryAndSourceType(string entryOrGuid, SourceTypes sourceType, bool showError = true, bool promptCreateIfNoneFound = false)
        {
            List<SmartScript> smartScriptsToReturn = new List<SmartScript>();

            try
            {
                List<SmartScript> smartScripts = await SAI_Editor_Manager.Instance.worldDatabase.GetSmartScripts(XConverter.ToInt32(entryOrGuid), (int)sourceType);

                if (smartScripts == null)
                {
                    if (showError)
                    {
                        bool showNormalErrorMessage = false;
                        string message = String.Format("The entryorguid '{0}' could not be found in the smart_scripts table for the given source_type!", entryOrGuid);
                        smartScripts = await SAI_Editor_Manager.Instance.worldDatabase.GetSmartScriptsWithoutSourceType(XConverter.ToInt32(entryOrGuid), (int)sourceType);

                        if (smartScripts != null)
                        {
                            message += "\n\nA script was found with this entry using sourcetype " + smartScripts[0].source_type + " (" + GetSourceTypeString((SourceTypes)smartScripts[0].source_type) + "). Do you wish to load this instead?";
                            DialogResult dialogResult = MessageBox.Show(message, "No scripts found!", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

                            if (dialogResult == DialogResult.Yes)
                            {
                                textBoxEntryOrGuid.Text = smartScripts[0].entryorguid.ToString();
                                comboBoxSourceType.SelectedIndex = GetIndexBySourceType((SourceTypes)smartScripts[0].source_type);
                                TryToLoadScript();
                            }
                        }
                        else
                        {
                            switch (sourceType)
                            {
                                case SourceTypes.SourceTypeCreature:
                                    //! Get `id` from `creature` and check it for SAI
                                    if (XConverter.ToInt32(entryOrGuid) < 0) //! Guid
                                    {
                                        int entry = await SAI_Editor_Manager.Instance.worldDatabase.GetCreatureIdByGuid(-XConverter.ToInt32(entryOrGuid));
                                        smartScripts = await SAI_Editor_Manager.Instance.worldDatabase.GetSmartScripts(entry, (int)SourceTypes.SourceTypeCreature);

                                        if (smartScripts != null)
                                        {
                                            message += "\n\nA script was not found for this guid but we did find one using the entry of the guid (" + smartScripts[0].entryorguid + "). Do you wish to load this instead?";
                                            DialogResult dialogResult = MessageBox.Show(message, "No scripts found!", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

                                            if (dialogResult == DialogResult.Yes)
                                            {
                                                textBoxEntryOrGuid.Text = smartScripts[0].entryorguid.ToString();
                                                comboBoxSourceType.SelectedIndex = GetIndexBySourceType(SourceTypes.SourceTypeCreature);
                                                TryToLoadScript();
                                            }
                                        }
                                        else
                                            showNormalErrorMessage = true;
                                    }
                                    //! Get all `guid` instances from `creature` for the given `id` and allow user to select a script
                                    else //! Non-guid (entry)
                                    {
                                        int actualEntry = XConverter.ToInt32(entryOrGuid);
                                        List<Creature> creatures = await SAI_Editor_Manager.Instance.worldDatabase.GetCreaturesById(actualEntry);

                                        if (creatures != null)
                                        {
                                            List<List<SmartScript>> creaturesWithSmartAi = new List<List<SmartScript>>();

                                            foreach (Creature creature in creatures)
                                            {
                                                smartScripts = await SAI_Editor_Manager.Instance.worldDatabase.GetSmartScripts(-creature.guid, (int)SourceTypes.SourceTypeCreature);

                                                if (smartScripts != null)
                                                    creaturesWithSmartAi.Add(smartScripts);
                                            }

                                            if (creaturesWithSmartAi.Count > 0)
                                            {
                                                message += "\n\nA script was not found for this entry but we did find script(s) for guid(s) spawned under this entry. Do you wish to select one of these instead? (you can pick one out of all guid-scripts for this entry)";
                                                DialogResult dialogResult = MessageBox.Show(message, "No scripts found!", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

                                                if (dialogResult == DialogResult.Yes)
                                                    using (SelectSmartScriptForm selectSmartScriptForm = new SelectSmartScriptForm(creaturesWithSmartAi))
                                                        selectSmartScriptForm.ShowDialog(this);
                                            }
                                            else
                                                showNormalErrorMessage = true;
                                        }
                                        else
                                            showNormalErrorMessage = true;
                                    }
                                    break;
                                case SourceTypes.SourceTypeGameobject:
                                    //! Get `id` from `gameobject` and check it for SAI
                                    if (XConverter.ToInt32(entryOrGuid) < 0) //! Guid
                                    {
                                        int entry = await SAI_Editor_Manager.Instance.worldDatabase.GetGameobjectIdByGuid(-XConverter.ToInt32(entryOrGuid));
                                        smartScripts = await SAI_Editor_Manager.Instance.worldDatabase.GetSmartScripts(entry, (int)SourceTypes.SourceTypeGameobject);

                                        if (smartScripts != null)
                                        {
                                            message += "\n\nA script was not found for this guid but we did find one using the entry of the guid (" + smartScripts[0].entryorguid + "). Do you wish to load this instead?";
                                            DialogResult dialogResult = MessageBox.Show(message, "No scripts found!", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

                                            if (dialogResult == DialogResult.Yes)
                                            {
                                                textBoxEntryOrGuid.Text = smartScripts[0].entryorguid.ToString();
                                                comboBoxSourceType.SelectedIndex = GetIndexBySourceType(SourceTypes.SourceTypeGameobject);
                                                TryToLoadScript();
                                            }
                                        }
                                        else
                                            showNormalErrorMessage = true;
                                    }
                                    //! Get all `guid` instances from `gameobject` for the given `id` and allow user to select a script
                                    else //! Non-guid (entry)
                                    {
                                        int actualEntry = XConverter.ToInt32(entryOrGuid);
                                        List<Gameobject> gameobjects = await SAI_Editor_Manager.Instance.worldDatabase.GetGameobjectsById(actualEntry);

                                        if (gameobjects != null)
                                        {
                                            List<List<SmartScript>> gameobjectsWithSmartAi = new List<List<SmartScript>>();

                                            foreach (Gameobject gameobject in gameobjects)
                                            {
                                                smartScripts = await SAI_Editor_Manager.Instance.worldDatabase.GetSmartScripts(-gameobject.guid, (int)SourceTypes.SourceTypeGameobject);

                                                if (smartScripts != null)
                                                    gameobjectsWithSmartAi.Add(smartScripts);
                                            }

                                            if (gameobjectsWithSmartAi.Count > 0)
                                            {
                                                message += "\n\nA script was not found for this entry but we did find script(s) for guid(s) spawned under this entry. Do you wish to select one of these instead? (you can pick one out of all guid-scripts for this entry)";
                                                DialogResult dialogResult = MessageBox.Show(message, "No scripts found!", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);

                                                if (dialogResult == DialogResult.Yes)
                                                    using (SelectSmartScriptForm selectSmartScriptForm = new SelectSmartScriptForm(gameobjectsWithSmartAi))
                                                        selectSmartScriptForm.ShowDialog(this);
                                            }
                                            else
                                                showNormalErrorMessage = true;
                                        }
                                        else
                                            showNormalErrorMessage = true;
                                    }
                                    break;
                                default:
                                    showNormalErrorMessage = true;
                                    break;
                            }
                        }

                        if (showNormalErrorMessage)
                        {
                            if (promptCreateIfNoneFound)
                            {
                                DialogResult dialogResult = MessageBox.Show(message + "\n\nDo you want to create a new script using this entryorguid?", "No scripts found!", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                                if (dialogResult == DialogResult.Yes)
                                    TryToCreateScript();
                            }
                            else
                                MessageBox.Show(message, "No scripts found!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }

                    pictureBoxLoadScript.Enabled = textBoxEntryOrGuid.Text.Length > 0 && Settings.Default.UseWorldDatabase;
                    pictureBoxCreateScript.Enabled = textBoxEntryOrGuid.Text.Length > 0;
                    return new List<SmartScript>();
                }

                for (int i = 0; i < smartScripts.Count; ++i)
                {
                    smartScriptsToReturn.Add(smartScripts[i]);

                    if (!checkBoxListActionlistsOrEntries.Checked || !checkBoxListActionlistsOrEntries.Enabled)
                        continue;

                    if (i == smartScripts.Count - 1 && originalEntryOrGuidAndSourceType.sourceType == SourceTypes.SourceTypeScriptedActionlist)
                    {
                        List<EntryOrGuidAndSourceType> timedActionListOrEntries = await SAI_Editor_Manager.Instance.GetTimedActionlistsOrEntries(smartScripts[i], sourceType);

                        //if (timedActionListOrEntries.sourceTypeOfEntry != SourceTypes.SourceTypeScriptedActionlist)
                        {
                            foreach (EntryOrGuidAndSourceType entryOrGuidAndSourceType in timedActionListOrEntries)
                            {
                                if (entryOrGuidAndSourceType.sourceType == SourceTypes.SourceTypeScriptedActionlist)
                                    continue;

                                List<SmartScript> newSmartScripts = await GetSmartScriptsForEntryAndSourceType(entryOrGuidAndSourceType.entryOrGuid.ToString(), entryOrGuidAndSourceType.sourceType);

                                if (newSmartScripts != null)
                                    foreach (SmartScript item in newSmartScripts.Where(item => !ListContainsSmartScript(smartScriptsToReturn, item))) smartScriptsToReturn.Add(item);

                                pictureBoxCreateScript.Enabled = textBoxEntryOrGuid.Text.Length > 0;
                            }
                        }
                    }

                    if (sourceType == originalEntryOrGuidAndSourceType.sourceType && originalEntryOrGuidAndSourceType.sourceType != SourceTypes.SourceTypeScriptedActionlist)
                    {
                        List<EntryOrGuidAndSourceType> timedActionListOrEntries = await SAI_Editor_Manager.Instance.GetTimedActionlistsOrEntries(smartScripts[i], sourceType);

                        foreach (EntryOrGuidAndSourceType entryOrGuidAndSourceType in timedActionListOrEntries)
                        {
                            List<SmartScript> newSmartScripts = await GetSmartScriptsForEntryAndSourceType(entryOrGuidAndSourceType.entryOrGuid.ToString(), entryOrGuidAndSourceType.sourceType);

                            foreach (SmartScript item in newSmartScripts.Where(item => !ListContainsSmartScript(smartScriptsToReturn, item))) smartScriptsToReturn.Add(item);

                            pictureBoxCreateScript.Enabled = textBoxEntryOrGuid.Text.Length > 0;
                        }
                    }
                }

                foreach (ColumnHeader header in listViewSmartScripts.Columns)
                    header.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
            }
            catch (Exception ex)
            {
                if (showError)
                    MessageBox.Show(ex.Message, "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            pictureBoxLoadScript.Enabled = textBoxEntryOrGuid.Text.Length > 0 && Settings.Default.UseWorldDatabase;
            pictureBoxCreateScript.Enabled = textBoxEntryOrGuid.Text.Length > 0;
            return smartScriptsToReturn;
        }

        bool ListContainsSmartScript(List<SmartScript> smartScriptsToReturn, SmartScript item)
        {
            return smartScriptsToReturn.Any(itemToReturn => itemToReturn.entryorguid == item.entryorguid && itemToReturn.id == item.id);
        }

        private void menuItemExit_Click(object sender, System.EventArgs e)
        {
            if (formState == FormState.FormStateMain)
                TryCloseApplication();
        }

        private void TryCloseApplication()
        {
            if (!Settings.Default.PromptToQuit || DialogResult.Yes == MessageBox.Show("Are you sure you want to quit?", "Are you sure?", MessageBoxButtons.YesNo, MessageBoxIcon.Question))
                Close();
        }

        private void menuItemSettings_Click(object sender, EventArgs e)
        {
            if (formState != FormState.FormStateMain)
                return;

            using (SettingsForm settingsForm = new SettingsForm())
                settingsForm.ShowDialog(this);
        }

        private void menuItemAbout_Click(object sender, EventArgs e)
        {
            if (formState != FormState.FormStateMain)
                return;

            using (AboutForm aboutForm = new AboutForm())
                aboutForm.ShowDialog(this);
        }

        private void listViewSmartScripts_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            menuItemDeleteSelectedRow.Enabled = listViewSmartScripts.SelectedItems.Count > 0;
            menuItemGenerateSql.Enabled = listViewSmartScripts.SelectedItems.Count > 0;
            buttonGenerateSql.Enabled = listViewSmartScripts.SelectedItems.Count > 0;
            menuitemLoadSelectedEntry.Enabled = listViewSmartScripts.SelectedItems.Count > 0;
            menuItemDuplicateRow.Enabled = listViewSmartScripts.SelectedItems.Count > 0;
            menuItemGenerateComment.Enabled = listViewSmartScripts.SelectedItems.Count > 0;
            menuItemCopySelectedRow.Enabled = listViewSmartScripts.SelectedItems.Count > 0;

            if (!e.IsSelected)
                return;

            FillFieldsBasedOnSelectedScript();

            if (Settings.Default.ChangeStaticInfo)
                checkBoxListActionlistsOrEntries.Text = listViewSmartScripts.SelectedItems[0].SubItems[1].Text == "9" ? "List entries too" : "List actionlists too";
        }

        private void FillFieldsBasedOnSelectedScript()
        {
            try
            {
                updatingFieldsBasedOnSelectedScript = true;
                SmartScript selectedScript = listViewSmartScripts.SelectedSmartScript;

                if (Settings.Default.ChangeStaticInfo)
                {
                    textBoxEntryOrGuid.Text = selectedScript.entryorguid.ToString();
                    comboBoxSourceType.SelectedIndex = GetIndexBySourceType((SourceTypes)selectedScript.source_type);
                }

                textBoxId.Text = selectedScript.id.ToString();
                textBoxLinkTo.Text = selectedScript.link.ToString();
                textBoxLinkFrom.Text = GetLinkFromForSelection();

                int event_type = selectedScript.event_type;
                comboBoxEventType.SelectedIndex = event_type;
                textBoxEventPhasemask.Text = selectedScript.event_phase_mask.ToString();
                textBoxEventChance.Text = selectedScript.event_chance.ToString();
                textBoxEventFlags.Text = selectedScript.event_flags.ToString();

                //! Event parameters
                textBoxEventParam1.Text = selectedScript.event_param1.ToString();
                textBoxEventParam2.Text = selectedScript.event_param2.ToString();
                textBoxEventParam3.Text = selectedScript.event_param3.ToString();
                textBoxEventParam4.Text = selectedScript.event_param4.ToString();
                labelEventParam1.Text = SAI_Editor_Manager.Instance.GetParameterStringById(event_type, 1, ScriptTypeId.ScriptTypeEvent);
                labelEventParam2.Text = SAI_Editor_Manager.Instance.GetParameterStringById(event_type, 2, ScriptTypeId.ScriptTypeEvent);
                labelEventParam3.Text = SAI_Editor_Manager.Instance.GetParameterStringById(event_type, 3, ScriptTypeId.ScriptTypeEvent);
                labelEventParam4.Text = SAI_Editor_Manager.Instance.GetParameterStringById(event_type, 4, ScriptTypeId.ScriptTypeEvent);

                if (!Settings.Default.ShowTooltipsPermanently)
                {
                    AddTooltip(comboBoxEventType, comboBoxEventType.SelectedItem.ToString(), SAI_Editor_Manager.Instance.GetScriptTypeTooltipById(event_type, ScriptTypeId.ScriptTypeEvent));
                    AddTooltip(labelEventParam1, labelEventParam1.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(event_type, 1, ScriptTypeId.ScriptTypeEvent));
                    AddTooltip(labelEventParam2, labelEventParam2.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(event_type, 2, ScriptTypeId.ScriptTypeEvent));
                    AddTooltip(labelEventParam3, labelEventParam3.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(event_type, 3, ScriptTypeId.ScriptTypeEvent));
                    AddTooltip(labelEventParam4, labelEventParam4.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(event_type, 4, ScriptTypeId.ScriptTypeEvent));
                }

                //! Action parameters
                int action_type = selectedScript.action_type;
                comboBoxActionType.SelectedIndex = action_type;
                textBoxActionParam1.Text = selectedScript.action_param1.ToString();
                textBoxActionParam2.Text = selectedScript.action_param2.ToString();
                textBoxActionParam3.Text = selectedScript.action_param3.ToString();
                textBoxActionParam4.Text = selectedScript.action_param4.ToString();
                textBoxActionParam5.Text = selectedScript.action_param5.ToString();
                textBoxActionParam6.Text = selectedScript.action_param6.ToString();
                labelActionParam1.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 1, ScriptTypeId.ScriptTypeAction);
                labelActionParam2.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 2, ScriptTypeId.ScriptTypeAction);
                labelActionParam3.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 3, ScriptTypeId.ScriptTypeAction);
                labelActionParam4.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 4, ScriptTypeId.ScriptTypeAction);
                labelActionParam5.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 5, ScriptTypeId.ScriptTypeAction);
                labelActionParam6.Text = SAI_Editor_Manager.Instance.GetParameterStringById(action_type, 6, ScriptTypeId.ScriptTypeAction);

                if (!Settings.Default.ShowTooltipsPermanently)
                {
                    AddTooltip(comboBoxActionType, comboBoxActionType.SelectedItem.ToString(), SAI_Editor_Manager.Instance.GetScriptTypeTooltipById(action_type, ScriptTypeId.ScriptTypeAction));
                    AddTooltip(labelActionParam1, labelActionParam1.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 1, ScriptTypeId.ScriptTypeAction));
                    AddTooltip(labelActionParam2, labelActionParam2.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 2, ScriptTypeId.ScriptTypeAction));
                    AddTooltip(labelActionParam3, labelActionParam3.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 3, ScriptTypeId.ScriptTypeAction));
                    AddTooltip(labelActionParam4, labelActionParam4.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 4, ScriptTypeId.ScriptTypeAction));
                    AddTooltip(labelActionParam5, labelActionParam5.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 5, ScriptTypeId.ScriptTypeAction));
                    AddTooltip(labelActionParam6, labelActionParam6.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(action_type, 6, ScriptTypeId.ScriptTypeAction));
                }

                //! Target parameters
                int target_type = selectedScript.target_type;
                comboBoxTargetType.SelectedIndex = target_type;
                textBoxTargetParam1.Text = selectedScript.target_param1.ToString();
                textBoxTargetParam2.Text = selectedScript.target_param2.ToString();
                textBoxTargetParam3.Text = selectedScript.target_param3.ToString();
                labelTargetParam1.Text = SAI_Editor_Manager.Instance.GetParameterStringById(target_type, 1, ScriptTypeId.ScriptTypeTarget);
                labelTargetParam2.Text = SAI_Editor_Manager.Instance.GetParameterStringById(target_type, 2, ScriptTypeId.ScriptTypeTarget);
                labelTargetParam3.Text = SAI_Editor_Manager.Instance.GetParameterStringById(target_type, 3, ScriptTypeId.ScriptTypeTarget);

                if (!Settings.Default.ShowTooltipsPermanently)
                {
                    AddTooltip(comboBoxTargetType, comboBoxTargetType.SelectedItem.ToString(), SAI_Editor_Manager.Instance.GetScriptTypeTooltipById(target_type, ScriptTypeId.ScriptTypeTarget));
                    AddTooltip(labelTargetParam1, labelTargetParam1.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(target_type, 1, ScriptTypeId.ScriptTypeTarget));
                    AddTooltip(labelTargetParam2, labelTargetParam2.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(target_type, 2, ScriptTypeId.ScriptTypeTarget));
                    AddTooltip(labelTargetParam3, labelTargetParam3.Text, SAI_Editor_Manager.Instance.GetParameterTooltipById(target_type, 3, ScriptTypeId.ScriptTypeTarget));
                }

                textBoxTargetX.Text = selectedScript.target_x.ToString();
                textBoxTargetY.Text = selectedScript.target_y.ToString();
                textBoxTargetZ.Text = selectedScript.target_z.ToString();
                textBoxTargetO.Text = selectedScript.target_o.ToString();
                textBoxComments.Text = selectedScript.comment;

                AdjustAllParameterFields(event_type, action_type, target_type);
                updatingFieldsBasedOnSelectedScript = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetLinkFromForSelection()
        {
            SmartScript selectedScript = listViewSmartScripts.SelectedSmartScript;

            foreach (SmartScript smartScript in listViewSmartScripts.SmartScripts)
            {
                if (smartScript.entryorguid != selectedScript.entryorguid || smartScript.source_type != selectedScript.source_type)
                    continue;

                if (smartScript.link > 0 && smartScript.link == listViewSmartScripts.SelectedSmartScript.id)
                    return smartScript.id.ToString();
            }

            return "None";
        }

        private void AdjustAllParameterFields(int event_type, int action_type, int target_type)
        {
            SetVisibilityOfAllParamButtons(false);

            switch ((SmartEvent)event_type)
            {
                case SmartEvent.SMART_EVENT_SPELLHIT: //! Spell entry & Spell school
                case SmartEvent.SMART_EVENT_SPELLHIT_TARGET: //! Spell entry & Spell school
                case SmartEvent.SMART_EVENT_GOSSIP_SELECT: //! Gossip menu id & gossip id
                    buttonEventParamOneSearch.Visible = true;
                    buttonEventParamTwoSearch.Visible = true;
                    break;
                case SmartEvent.SMART_EVENT_RESPAWN:
                    buttonEventParamOneSearch.Visible = true; //! Respawn condition (SMART_SCRIPT_RESPAWN_CONDITION_MAP / SMART_SCRIPT_RESPAWN_CONDITION_AREA)
                    buttonEventParamTwoSearch.Visible = true; //! Map entry
                    buttonEventParamThreeSearch.Visible = true; //! Zone entry
                    break;
                case SmartEvent.SMART_EVENT_AREATRIGGER_ONTRIGGER: //! Areatrigger entry
                case SmartEvent.SMART_EVENT_GO_STATE_CHANGED: //! Go state
                case SmartEvent.SMART_EVENT_GAME_EVENT_START: //! Game event entry
                case SmartEvent.SMART_EVENT_GAME_EVENT_END: //! Game event entry
                case SmartEvent.SMART_EVENT_MOVEMENTINFORM: //! Movement type
                case SmartEvent.SMART_EVENT_FRIENDLY_MISSING_BUFF: //! Spell id
                case SmartEvent.SMART_EVENT_HAS_AURA: //! Spell id
                case SmartEvent.SMART_EVENT_TARGET_BUFFED: //! Spell id
                case SmartEvent.SMART_EVENT_SUMMON_DESPAWNED: //! Creature entry
                case SmartEvent.SMART_EVENT_SUMMONED_UNIT: //! Creature entry
                case SmartEvent.SMART_EVENT_ACCEPTED_QUEST: //! Quest id
                case SmartEvent.SMART_EVENT_REWARD_QUEST: //! Quest id
                case SmartEvent.SMART_EVENT_RECEIVE_EMOTE: //! Emote id
                    buttonEventParamOneSearch.Visible = true;
                    break;
                case SmartEvent.SMART_EVENT_TEXT_OVER: //! Creature entry
                    buttonEventParamTwoSearch.Visible = true;
                    break;
                case SmartEvent.SMART_EVENT_VICTIM_CASTING: //! Spell id
                    buttonEventParamThreeSearch.Visible = true;
                    break;
                case SmartEvent.SMART_EVENT_KILL: //! Creature entry
                    buttonEventParamFourSearch.Visible = true;
                    break;
            }

            switch ((SmartAction)action_type)
            {
                case SmartAction.SMART_ACTION_CAST: //! Spell entry & Cast flags
                case SmartAction.SMART_ACTION_INVOKER_CAST: //! Spell entry & Cast flags
                case SmartAction.SMART_ACTION_CALL_CASTEDCREATUREORGO: //! Creature entry & Spell entry
                case SmartAction.SMART_ACTION_SUMMON_CREATURE: //! Creature entry & Summon type
                case SmartAction.SMART_ACTION_SET_UNIT_FIELD_BYTES_1: //! Bytes1flags & Type
                case SmartAction.SMART_ACTION_REMOVE_UNIT_FIELD_BYTES_1: //! Bytes1flags & Type
                case SmartAction.SMART_ACTION_RANDOM_PHASE_RANGE: //! Event phase 1 & 2
                    buttonActionParamOneSearch.Visible = true;
                    buttonActionParamTwoSearch.Visible = true;
                    break;
                case SmartAction.SMART_ACTION_CROSS_CAST:
                    buttonActionParamOneSearch.Visible = true; //! Spell entry
                    buttonActionParamTwoSearch.Visible = true; //! Cast flags
                    buttonActionParamThreeSearch.Visible = true; //! Target type
                    break;
                case SmartAction.SMART_ACTION_WP_STOP: //! Quest entry
                case SmartAction.SMART_ACTION_INTERRUPT_SPELL: //! Spell entry
                case SmartAction.SMART_ACTION_SEND_GOSSIP_MENU: //! Gossip menu id & npc_text.id
                case SmartAction.SMART_ACTION_CALL_TIMED_ACTIONLIST: //! Timer type
                    buttonActionParamTwoSearch.Visible = true;
                    break;
                case SmartAction.SMART_ACTION_WP_START:
                    buttonActionParamTwoSearch.Visible = true; //! Waypoint entry
                    buttonActionParamFourSearch.Visible = true; //! Quest entry
                    buttonActionParamSixSearch.Visible = true; //! React state
                    break;
                case SmartAction.SMART_ACTION_FOLLOW:
                    buttonActionParamThreeSearch.Visible = true; //! Creature entry
                    buttonActionParamFourSearch.Visible = true; //! Creature entry
                    break;
                case SmartAction.SMART_ACTION_RANDOM_PHASE:  //! Event phase 1-6
                case SmartAction.SMART_ACTION_RANDOM_EMOTE: //! Emote entry 1-6
                    buttonActionParamOneSearch.Visible = true;
                    buttonActionParamTwoSearch.Visible = true;
                    buttonActionParamThreeSearch.Visible = true;
                    buttonActionParamFourSearch.Visible = true;
                    buttonActionParamFiveSearch.Visible = true;
                    buttonActionParamSixSearch.Visible = true;
                    break;
                case SmartAction.SMART_ACTION_EQUIP:
                    buttonActionParamOneSearch.Visible = true; //! Equipment entry
                    buttonActionParamThreeSearch.Visible = true; //! Item entry 1
                    buttonActionParamFourSearch.Visible = true; //! Item entry 2
                    buttonActionParamFiveSearch.Visible = true; //! Item entry 3
                    break;
                case SmartAction.SMART_ACTION_SET_FACTION: //! Faction entry
                case SmartAction.SMART_ACTION_EMOTE: //! Emote entry
                case SmartAction.SMART_ACTION_SET_EMOTE_STATE: //! Emote entry
                case SmartAction.SMART_ACTION_FAIL_QUEST: //! Quest entry
                case SmartAction.SMART_ACTION_ADD_QUEST: //! Quest entry
                case SmartAction.SMART_ACTION_CALL_AREAEXPLOREDOREVENTHAPPENS: //! Quest entry
                case SmartAction.SMART_ACTION_CALL_GROUPEVENTHAPPENS: //! Quest entry
                case SmartAction.SMART_ACTION_SET_REACT_STATE: //! Reactstate
                case SmartAction.SMART_ACTION_SOUND: //! Sound entry
                case SmartAction.SMART_ACTION_MORPH_TO_ENTRY_OR_MODEL: //! Creature entry
                case SmartAction.SMART_ACTION_KILLED_MONSTER: //! Creature entry
                case SmartAction.SMART_ACTION_UPDATE_TEMPLATE: //! Creature entry
                case SmartAction.SMART_ACTION_MOUNT_TO_ENTRY_OR_MODEL: //! Creature entry
                case SmartAction.SMART_ACTION_GO_SET_LOOT_STATE: //! Gameobject state
                case SmartAction.SMART_ACTION_SET_POWER: //! Power type
                case SmartAction.SMART_ACTION_ADD_POWER: //! Power type
                case SmartAction.SMART_ACTION_REMOVE_POWER: //! Power type
                case SmartAction.SMART_ACTION_SUMMON_GO: //! Gameobject entry
                case SmartAction.SMART_ACTION_SET_EVENT_PHASE: //! Event phase
                case SmartAction.SMART_ACTION_SET_PHASE_MASK: //! Ingame phase
                case SmartAction.SMART_ACTION_ADD_ITEM: //! Item entry
                case SmartAction.SMART_ACTION_REMOVE_ITEM: //! Item entry
                case SmartAction.SMART_ACTION_TELEPORT: //! Map id
                case SmartAction.SMART_ACTION_SUMMON_CREATURE_GROUP: //! Summons group id
                case SmartAction.SMART_ACTION_REMOVEAURASFROMSPELL: //! Spell id
                case SmartAction.SMART_ACTION_SET_SHEATH: //! Sheath state
                case SmartAction.SMART_ACTION_ACTIVATE_TAXI: //! Taxi path id
                case SmartAction.SMART_ACTION_SET_UNIT_FLAG: //! Unit flags
                case SmartAction.SMART_ACTION_REMOVE_UNIT_FLAG: //! Unit flags
                case SmartAction.SMART_ACTION_SET_GO_FLAG: //! Gameobject flags
                case SmartAction.SMART_ACTION_ADD_GO_FLAG: //! Gameobject flags
                case SmartAction.SMART_ACTION_REMOVE_GO_FLAG: //! Gameobject flags
                case SmartAction.SMART_ACTION_SET_DYNAMIC_FLAG: //! Dynamic flags
                case SmartAction.SMART_ACTION_ADD_DYNAMIC_FLAG: //! Dynamic flags
                case SmartAction.SMART_ACTION_REMOVE_DYNAMIC_FLAG: //! Dynamic flags
                case SmartAction.SMART_ACTION_ADD_AURA: //! Spell id
                case SmartAction.SMART_ACTION_SET_NPC_FLAG: //! Npc flags
                case SmartAction.SMART_ACTION_ADD_NPC_FLAG: //! Npc flags
                case SmartAction.SMART_ACTION_REMOVE_NPC_FLAG: //! Npc flags
                case SmartAction.SMART_ACTION_INSTALL_AI_TEMPLATE: //! AI template
                    buttonActionParamOneSearch.Visible = true;
                    break;
            }

            switch ((SmartTarget)target_type)
            {
                case SmartTarget.SMART_TARGET_CREATURE_GUID:
                    buttonTargetParamOneSearch.Visible = true; //! Creature guid
                    buttonTargetParamTwoSearch.Visible = true; //! Creature entry
                    break;
                case SmartTarget.SMART_TARGET_GAMEOBJECT_GUID:
                    buttonTargetParamOneSearch.Visible = true; //! Gameobject guid
                    buttonTargetParamTwoSearch.Visible = true; //! Gameobject entry
                    break;
                case SmartTarget.SMART_TARGET_CREATURE_RANGE: //! Creature entry
                case SmartTarget.SMART_TARGET_CREATURE_DISTANCE: //! Creature entry
                case SmartTarget.SMART_TARGET_CLOSEST_CREATURE: //! Creature entry
                case SmartTarget.SMART_TARGET_GAMEOBJECT_RANGE: //! Gameobject entry
                case SmartTarget.SMART_TARGET_GAMEOBJECT_DISTANCE: //! Gameobject entry
                case SmartTarget.SMART_TARGET_CLOSEST_GAMEOBJECT: //! Gameobject entry
                    buttonTargetParamOneSearch.Visible = true;
                    break;
            }
        }

        private void AddTooltip(Control control, string title, string text, ToolTipIcon icon = ToolTipIcon.Info, bool isBallon = true, bool active = true, int autoPopDelay = 2100000000, bool showAlways = true)
        {
            if (String.IsNullOrWhiteSpace(title) || String.IsNullOrWhiteSpace(text))
            {
                DetailedToolTip toolTipExistent = ToolTipHelper.GetExistingToolTip(control);

                if (toolTipExistent != null)
                    toolTipExistent.Active = false;

                return;
            }

            DetailedToolTip toolTip = ToolTipHelper.GetControlToolTip(control);
            toolTip.ToolTipIcon = icon;
            toolTip.ToolTipTitle = title;
            toolTip.IsBalloon = isBallon;
            toolTip.Active = active;
            toolTip.AutoPopDelay = autoPopDelay;
            toolTip.ShowAlways = showAlways;
            toolTip.SetToolTipText(control, text);
        }

        private void AddTooltip(string controlName, string title, string text, ToolTipIcon icon = ToolTipIcon.Info, bool isBallon = true, bool active = true, int autoPopDelay = 2100000000, bool showAlways = true)
        {
            Control[] controls = Controls.Find(controlName, true);

            if (controls.Length > 0)
                foreach (Control control in controls)
                    AddTooltip(control, title, text, icon, isBallon, active, autoPopDelay, showAlways);
        }

        private void textBoxEventTypeId_TextChanged(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(textBoxEventType.Text))
            {
                comboBoxEventType.SelectedIndex = 0;
                textBoxEventType.Text = "0";
                textBoxEventType.SelectionStart = 3; //! Set cursor position to end of the line
            }
            else
            {
                int eventType;
                Int32.TryParse(textBoxEventType.Text, out eventType);

                if (eventType > (int)MaxValues.MaxEventType)
                {
                    comboBoxEventType.SelectedIndex = (int)MaxValues.MaxEventType;
                    textBoxEventType.Text = ((int)MaxValues.MaxEventType).ToString();
                    textBoxEventType.SelectionStart = 3; //! Set cursor position to end of the line
                }
                else
                    comboBoxEventType.SelectedIndex = eventType;
            }
        }

        private void textBoxActionTypeId_TextChanged(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(textBoxActionType.Text))
            {
                comboBoxActionType.SelectedIndex = 0;
                textBoxActionType.Text = "0";
                textBoxActionType.SelectionStart = 3; //! Set cursor position to end of the line
            }
            else
            {
                int actionType;
                Int32.TryParse(textBoxActionType.Text, out actionType);

                if (actionType > (int)MaxValues.MaxActionType)
                {
                    comboBoxActionType.SelectedIndex = (int)MaxValues.MaxActionType;
                    textBoxActionType.Text = ((int)MaxValues.MaxActionType).ToString();
                    textBoxActionType.SelectionStart = 3; //! Set cursor position to end of the line
                }
                else
                    comboBoxActionType.SelectedIndex = actionType;
            }
        }

        private void textBoxTargetTypeId_TextChanged(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(textBoxTargetType.Text))
            {
                comboBoxTargetType.SelectedIndex = 0;
                textBoxTargetType.Text = "0";
                textBoxTargetType.SelectionStart = 3; //! Set cursor position to end of the line
            }
            else
            {
                int targetType;
                Int32.TryParse(textBoxTargetType.Text, out targetType);

                if (targetType > (int)MaxValues.MaxTargetType)
                {
                    comboBoxTargetType.SelectedIndex = (int)MaxValues.MaxTargetType;
                    textBoxTargetType.Text = ((int)MaxValues.MaxTargetType).ToString();
                    textBoxTargetType.SelectionStart = 3; //! Set cursor position to end of the line
                }
                else
                    comboBoxTargetType.SelectedIndex = targetType;
            }
        }

        private void menuOptionDeleteSelectedRow_Click(object sender, EventArgs e)
        {
            if (formState != FormState.FormStateMain || listViewSmartScripts.SelectedSmartScript == null)
                return;

            if (listViewSmartScripts.SelectedItems.Count <= 0)
            {
                MessageBox.Show("No rows were selected to delete!", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DeleteSelectedRow();
        }

        private void menuItemCopySelectedRowListView_Click(object sender, EventArgs e)
        {
            if (formState != FormState.FormStateMain || listViewSmartScripts.SelectedSmartScript == null)
                return;

            smartScriptsOnClipBoard.Add(listViewSmartScripts.SelectedSmartScript.Clone());
        }

        private void menuItemPasteLastCopiedRow_Click(object sender, EventArgs e)
        {
            if (formState != FormState.FormStateMain || listViewSmartScripts.SelectedSmartScript == null)
                return;

            if (smartScriptsOnClipBoard.Count <= 0)
            {
                MessageBox.Show("No smart scripts have been copied in this session!", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SmartScript newSmartScript = smartScriptsOnClipBoard.Last().Clone();
            listViewSmartScripts.AddSmartScript(newSmartScript);
        }

        private void DeleteSelectedRow()
        {
            if (listViewSmartScripts.SelectedItems.Count == 0)
                return;

            int prevSelectedIndex = listViewSmartScripts.SelectedItems[0].Index;

            if (listViewSmartScripts.SelectedItems[0].SubItems[0].Text == originalEntryOrGuidAndSourceType.entryOrGuid.ToString())
                if (listViewSmartScripts.SelectedItems[0].SubItems[2].Text == lastSmartScriptIdOfScript.ToString())
                    lastSmartScriptIdOfScript--;

            lastDeletedSmartScripts.Add(listViewSmartScripts.SelectedSmartScript.Clone());
            listViewSmartScripts.RemoveSmartScript(listViewSmartScripts.SelectedSmartScript);
            SetGenerateCommentsEnabled(listViewSmartScripts.Items.Count > 0 && Settings.Default.UseWorldDatabase);

            if (listViewSmartScripts.Items.Count <= 0)
                ResetFieldsToDefault(Settings.Default.ChangeStaticInfo);
            else
                ReSelectListViewItemWithPrevIndex(prevSelectedIndex);

            //! Need to do this if static info is changed
            pictureBoxCreateScript.Enabled = textBoxEntryOrGuid.Text.Length > 0;
        }

        private void SetGenerateCommentsEnabled(bool enabled)
        {
            buttonGenerateComments.Enabled = enabled;
            menuItemGenerateComment.Enabled = enabled;
        }

        private void ReSelectListViewItemWithPrevIndex(int prevIndex)
        {
            if (listViewSmartScripts.Items.Count > prevIndex)
                listViewSmartScripts.Items[prevIndex].Selected = true;
            else
                listViewSmartScripts.Items[prevIndex - 1].Selected = true;
        }

        private async void checkBoxListActionlists_CheckedChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.Items.Count == 0)
                return;

            buttonGenerateSql.Enabled = false;
            menuItemGenerateSql.Enabled = false;
            int prevSelectedIndex = listViewSmartScripts.SelectedItems.Count > 0 ? listViewSmartScripts.SelectedItems[0].Index : 0;

            if (checkBoxListActionlistsOrEntries.Checked)
            {
                List<SmartScript> smartScripts = await GetSmartScriptsForEntryAndSourceType(originalEntryOrGuidAndSourceType.entryOrGuid.ToString(), originalEntryOrGuidAndSourceType.sourceType);
                List<SmartScript> newSmartScripts = new List<SmartScript>();

                //! Only add the new smartscript if it doesn't yet exist
                foreach (SmartScript newSmartScript in smartScripts)
                    if (!listViewSmartScripts.Items.Cast<SmartScriptListViewItem>().Any(p => p.Script.entryorguid == newSmartScript.entryorguid && p.Script.id == newSmartScript.id))
                        listViewSmartScripts.AddSmartScript(newSmartScript);

                pictureBoxCreateScript.Enabled = textBoxEntryOrGuid.Text.Length > 0;
            }
            else
                RemoveNonOriginalScriptsFromView();

            HandleShowBasicInfo();

            if (listViewSmartScripts.Items.Count > prevSelectedIndex)
                listViewSmartScripts.Items[prevSelectedIndex].Selected = true;

            buttonGenerateSql.Enabled = listViewSmartScripts.Items.Count > 0;
            menuItemGenerateSql.Enabled = listViewSmartScripts.Items.Count > 0;
        }

        private void RemoveNonOriginalScriptsFromView()
        {
            List<SmartScript> smartScriptsToRemove = listViewSmartScripts.SmartScripts.Where(smartScript => smartScript.source_type != (int)originalEntryOrGuidAndSourceType.sourceType).ToList();

            foreach (SmartScript smartScript in smartScriptsToRemove)
                listViewSmartScripts.SmartScripts.Remove(smartScript);
        }

        public SourceTypes GetSourceTypeByIndex()
        {
            switch (comboBoxSourceType.SelectedIndex)
            {
                case 0: //! Creature
                case 1: //! Gameobject
                case 2: //! Areatrigger
                    return (SourceTypes)comboBoxSourceType.SelectedIndex;
                case 3: //! Actionlist
                    return SourceTypes.SourceTypeScriptedActionlist;
                default:
                    return SourceTypes.SourceTypeCreature; //! Default...
            }
        }

        public int GetIndexBySourceType(SourceTypes sourceType)
        {
            switch (sourceType)
            {
                case SourceTypes.SourceTypeCreature:
                case SourceTypes.SourceTypeGameobject:
                case SourceTypes.SourceTypeAreaTrigger:
                    return (int)sourceType;
                case SourceTypes.SourceTypeScriptedActionlist:
                    return 3;
                default:
                    return -1;
            }
        }

        public void pictureBoxLoadScript_Click(object sender, EventArgs e)
        {
            if (!pictureBoxLoadScript.Enabled || !Settings.Default.UseWorldDatabase)
                return;

            TryToLoadScript();
        }

        private void pictureBoxCreateScript_Click(object sender, EventArgs e)
        {
            if (!pictureBoxCreateScript.Enabled)
                return;

            if (String.IsNullOrWhiteSpace(textBoxEntryOrGuid.Text) || comboBoxSourceType.SelectedIndex == -1)
                return;

            TryToCreateScript();
        }

        public async void TryToCreateScript(bool fromNewLine = false)
        {
            if (listViewSmartScripts.Items.Count > 0)
            {
                DialogResult dialogResult = MessageBox.Show("There is already a script loaded at this moment. Do you want to overwrite this?\n\nWarning: overwriting means local unsaved changes will also be discarded!", "Are you sure?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (dialogResult != DialogResult.Yes)
                    return;

                ResetFieldsToDefault();
            }

            int entryorguid = 0;

            try
            {
                entryorguid = Int32.Parse(textBoxEntryOrGuid.Text);
            }
            catch (OverflowException)
            {
                MessageBox.Show("The entryorguid is either too big or too small.", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            catch (FormatException)
            {
                MessageBox.Show("The entryorguid field does not contain a valid number.", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            lastSmartScriptIdOfScript = 0;
            int source_type = (int)GetSourceTypeByIndex();
            string sourceTypeString = GetSourceTypeString((SourceTypes)source_type);

            if (!Settings.Default.UseWorldDatabase)
                goto SkipWorldDatabaseChecks;

            string aiName = await SAI_Editor_Manager.Instance.worldDatabase.GetObjectAiName(entryorguid, source_type);
            List<SmartScript> smartScripts = await SAI_Editor_Manager.Instance.worldDatabase.GetSmartScripts(entryorguid, source_type);

            //! Allow adding new lines even if the AIName is already set
            if ((SourceTypes)source_type == SourceTypes.SourceTypeAreaTrigger)
            {
                if (aiName != String.Empty)
                {
                    string errorMessage = "This areatrigger already has its ";

                    if (aiName != "SmartTrigger")
                        errorMessage += "ScriptName set to '" + aiName + "'";
                    else
                        errorMessage += "AIName set (for SmartAI)! Do you want to load it instead?";

                    DialogResult dialogResult = MessageBox.Show(errorMessage, "Something went wrong", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (dialogResult == DialogResult.Yes)
                        TryToLoadScript();

                    return;
                }
            }
            else
            {
                if (aiName != String.Empty)
                {
                    string strAlreadyHasAiName = String.Empty;
                    bool aiNameIsSmart = SAI_Editor_Manager.Instance.IsAiNameSmartAi(aiName);

                    if (aiNameIsSmart)
                    {
                        if (smartScripts == null || smartScripts.Count == 0)
                            goto SkipWorldDatabaseChecks;

                        if (fromNewLine)
                            goto SkipAiNameAndScriptNameChecks;

                        strAlreadyHasAiName += "This " + sourceTypeString + " already has its AIName set to '" + aiName + "'";
                        strAlreadyHasAiName += "! Do you want to load it instead?";
                    }
                    else
                    {
                        strAlreadyHasAiName += "This " + sourceTypeString + " already has its AIName set to '" + aiName + "'";
                        strAlreadyHasAiName += " and can therefore not have any SmartAI. Do you want to get rid of this AIName right now?";
                    }

                    DialogResult dialogResult = MessageBox.Show(strAlreadyHasAiName, "Something went wrong", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (dialogResult == DialogResult.Yes)
                    {
                        if (!aiNameIsSmart)
                        {
                            //! We don't have to target areatrigger_scripts here, as we've already done this a few lines up
                            string sqlOutput = "UPDATE `" + GetTemplateTableBySourceType((SourceTypes)source_type) + "` SET `AIName`=" + '"' + '"' + " WHERE `entry`=" + entryorguid + ";\n";

                            using (SqlOutputForm sqlOutputForm = new SqlOutputForm(sqlOutput))
                                sqlOutputForm.ShowDialog(this);
                        }
                        else
                            TryToLoadScript();
                    }

                    return;
                }

                string scriptName = await SAI_Editor_Manager.Instance.worldDatabase.GetObjectScriptName(entryorguid, source_type);

                if (scriptName != String.Empty)
                {
                    MessageBox.Show("This " + sourceTypeString + " already has a ScriptName set (to '" + scriptName + "')!", "Something went wrong", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

        SkipAiNameAndScriptNameChecks:

            if (smartScripts != null && smartScripts.Count > 0)
            {
                string errorMessage = "This " + sourceTypeString + " already has smart scripts";// (without its AIName set to SmartAI)! Do you want to load it instead?";

                if ((SourceTypes)source_type != SourceTypes.SourceTypeScriptedActionlist)
                    errorMessage += " (without its AIName set to SmartAI)!";
                else
                    errorMessage += "!";

                errorMessage += " Do you want to load it instead?";
                DialogResult dialogResult = MessageBox.Show(errorMessage, "Something went wrong", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                if (dialogResult == DialogResult.Yes)
                    TryToLoadScript();

                return;
            }

        SkipWorldDatabaseChecks:
            buttonNewLine.Enabled = false;
            checkBoxListActionlistsOrEntries.Text = GetSourceTypeByIndex() == SourceTypes.SourceTypeScriptedActionlist ? "List entries too" : "List actionlists too";
            pictureBoxLoadScript.Enabled = false;
            pictureBoxCreateScript.Enabled = false;

            originalEntryOrGuidAndSourceType.entryOrGuid = entryorguid;
            originalEntryOrGuidAndSourceType.sourceType = (SourceTypes)source_type;

            listViewSmartScripts.ReplaceSmartScripts(new List<SmartScript>());

            SmartScript newSmartScript = new SmartScript();
            newSmartScript.entryorguid = entryorguid;
            newSmartScript.source_type = source_type;

            if (checkBoxLockEventId.Checked)
                newSmartScript.id = 0;
            else
                newSmartScript.id = -1;

            newSmartScript.link = XConverter.ToInt32(textBoxLinkTo.Text);
            newSmartScript.event_type = XConverter.ToInt32(textBoxEventType.Text);
            newSmartScript.event_phase_mask = XConverter.ToInt32(textBoxEventPhasemask.Text);
            newSmartScript.event_chance = XConverter.ToInt32(textBoxEventChance.Value);
            newSmartScript.event_flags = XConverter.ToInt32(textBoxEventFlags.Text);
            newSmartScript.event_param1 = XConverter.ToInt32(textBoxEventParam1.Text);
            newSmartScript.event_param2 = XConverter.ToInt32(textBoxEventParam2.Text);
            newSmartScript.event_param3 = XConverter.ToInt32(textBoxEventParam3.Text);
            newSmartScript.event_param4 = XConverter.ToInt32(textBoxEventParam4.Text);
            newSmartScript.action_type = XConverter.ToInt32(textBoxActionType.Text);
            newSmartScript.action_param1 = XConverter.ToInt32(textBoxActionParam1.Text);
            newSmartScript.action_param2 = XConverter.ToInt32(textBoxActionParam2.Text);
            newSmartScript.action_param3 = XConverter.ToInt32(textBoxActionParam3.Text);
            newSmartScript.action_param4 = XConverter.ToInt32(textBoxActionParam4.Text);
            newSmartScript.action_param5 = XConverter.ToInt32(textBoxActionParam5.Text);
            newSmartScript.action_param6 = XConverter.ToInt32(textBoxActionParam6.Text);
            newSmartScript.target_type = XConverter.ToInt32(textBoxTargetType.Text);
            newSmartScript.target_param1 = XConverter.ToInt32(textBoxTargetParam1.Text);
            newSmartScript.target_param2 = XConverter.ToInt32(textBoxTargetParam2.Text);
            newSmartScript.target_param3 = XConverter.ToInt32(textBoxTargetParam3.Text);
            newSmartScript.target_x = XConverter.ToDouble(textBoxTargetX.Text);
            newSmartScript.target_y = XConverter.ToDouble(textBoxTargetY.Text);
            newSmartScript.target_z = XConverter.ToDouble(textBoxTargetZ.Text);
            newSmartScript.target_o = XConverter.ToDouble(textBoxTargetO.Text);

            if (Settings.Default.GenerateComments && Settings.Default.UseWorldDatabase)
                newSmartScript.comment = await CommentGenerator.Instance.GenerateCommentFor(newSmartScript, originalEntryOrGuidAndSourceType);
            else if (textBoxComments.Text.Contains(" - Event - Action (phase) (dungeon difficulty)"))
                newSmartScript.comment = SAI_Editor_Manager.Instance.GetDefaultCommentForSourceType((SourceTypes)newSmartScript.source_type);
            else
                newSmartScript.comment = textBoxComments.Text;

            listViewSmartScripts.AddSmartScript(newSmartScript);

            HandleShowBasicInfo();

            listViewSmartScripts.Items[0].Selected = true;
            listViewSmartScripts.Select();

            buttonNewLine.Enabled = textBoxEntryOrGuid.Text.Length > 0;
            SetGenerateCommentsEnabled(listViewSmartScripts.Items.Count > 0 && Settings.Default.UseWorldDatabase);
            pictureBoxLoadScript.Enabled = textBoxEntryOrGuid.Text.Length > 0 && Settings.Default.UseWorldDatabase;
            pictureBoxCreateScript.Enabled = textBoxEntryOrGuid.Text.Length > 0;
        }

        private string GetTemplateTableBySourceType(SourceTypes sourceType)
        {
            switch (sourceType)
            {
                case SourceTypes.SourceTypeCreature:
                    return "creature_template";
                case SourceTypes.SourceTypeGameobject:
                    return "gameobject_template";
            }

            return "<unknown template table>";
        }

        public async void TryToLoadScript(int entryorguid = -1, SourceTypes sourceType = SourceTypes.SourceTypeNone, bool showErrorIfNoneFound = true, bool promptCreateIfNoneFound = false)
        {
            listViewSmartScripts.ReplaceSmartScripts(new List<SmartScript>());
            ResetFieldsToDefault();

            if (String.IsNullOrEmpty(textBoxEntryOrGuid.Text))
                return;

            buttonGenerateSql.Enabled = false;
            menuItemGenerateSql.Enabled = false;
            pictureBoxLoadScript.Enabled = false;
            pictureBoxCreateScript.Enabled = false;
            lastSmartScriptIdOfScript = 0;

            if (entryorguid != -1 && sourceType != SourceTypes.SourceTypeNone)
            {
                originalEntryOrGuidAndSourceType.entryOrGuid = entryorguid;
                originalEntryOrGuidAndSourceType.sourceType = sourceType;
                textBoxEntryOrGuid.Text = entryorguid.ToString();
                comboBoxSourceType.SelectedIndex = GetIndexBySourceType(sourceType);
            }
            else
            {
                try
                {
                    originalEntryOrGuidAndSourceType.entryOrGuid = Int32.Parse(textBoxEntryOrGuid.Text);
                }
                catch (OverflowException)
                {
                    MessageBox.Show("The entryorguid is either too big or too small.", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                catch (FormatException)
                {
                    MessageBox.Show("The entryorguid field does not contain a valid number.", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                originalEntryOrGuidAndSourceType.sourceType = GetSourceTypeByIndex();
            }

            List<SmartScript> smartScripts = await GetSmartScriptsForEntryAndSourceType(originalEntryOrGuidAndSourceType.entryOrGuid.ToString(), originalEntryOrGuidAndSourceType.sourceType, showErrorIfNoneFound, promptCreateIfNoneFound);
            listViewSmartScripts.ReplaceSmartScripts(smartScripts);
            checkBoxListActionlistsOrEntries.Text = originalEntryOrGuidAndSourceType.sourceType == SourceTypes.SourceTypeScriptedActionlist ? "List entries too" : "List actionlists too";

            buttonNewLine.Enabled = false;
            SetGenerateCommentsEnabled(listViewSmartScripts.Items.Count > 0 && Settings.Default.UseWorldDatabase);
            HandleShowBasicInfo();

            if (listViewSmartScripts.Items.Count > 0)
            {
                SortListView(SortOrder.Ascending, 1);
                listViewSmartScripts.Items[0].Selected = true;
                listViewSmartScripts.Select(); //! Sets the focus on the listview

                if (checkBoxListActionlistsOrEntries.Enabled && checkBoxListActionlistsOrEntries.Checked)
                {
                    foreach (ListViewItem item in listViewSmartScripts.Items)
                        if (item.Text == originalEntryOrGuidAndSourceType.entryOrGuid.ToString())
                            lastSmartScriptIdOfScript = XConverter.ToInt32(item.SubItems[2].Text);
                }
                else
                    lastSmartScriptIdOfScript = XConverter.ToInt32(listViewSmartScripts.Items[listViewSmartScripts.Items.Count - 1].SubItems[2].Text);
            }

            buttonNewLine.Enabled = textBoxEntryOrGuid.Text.Length > 0;
            buttonGenerateSql.Enabled = listViewSmartScripts.Items.Count > 0;
            menuItemGenerateSql.Enabled = listViewSmartScripts.Items.Count > 0;
            pictureBoxCreateScript.Enabled = textBoxEntryOrGuid.Text.Length > 0;
        }

        private void numericField_KeyPress(object sender, KeyPressEventArgs e)
        {
            //! Only allow typing keys that are numbers
            //! Insert is '-'
            //if (!Char.IsNumber(e.KeyChar) && e.KeyChar != (char)Keys.Back && e.KeyChar != (char)Keys.ControlKey && e.KeyChar != (char)Keys.Insert)
            //    e.Handled = true;
        }

        private void buttonSearchPhasemask_Click(object sender, EventArgs e)
        {
            using (MultiSelectForm<SmartPhaseMasks> multiSelectForm = new MultiSelectForm<SmartPhaseMasks>(textBoxEventPhasemask))
                multiSelectForm.ShowDialog(this);
        }

        private void buttonSelectEventFlag_Click(object sender, EventArgs e)
        {
            using (MultiSelectForm<SmartEventFlags> multiSelectForm = new MultiSelectForm<SmartEventFlags>(textBoxEventFlags))
                multiSelectForm.ShowDialog(this);
        }

        private async void buttonSearchWorldDb_Click(object sender, EventArgs e)
        {
            List<string> databaseNames = await SAI_Editor_Manager.Instance.GetDatabasesInConnection(textBoxHost.Text, textBoxUsername.Text, XConverter.ToUInt32(textBoxPort.Text), textBoxPassword.Text);

            if (databaseNames != null && databaseNames.Count > 0)
                using (var selectDatabaseForm = new SelectDatabaseForm(databaseNames, textBoxWorldDatabase))
                    selectDatabaseForm.ShowDialog(this);
        }

        private void listViewSmartScripts_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            //! Don't use the SortListView method here
            listViewSmartScripts.ListViewItemSorter = lvwColumnSorter;

            if (e.Column != lvwColumnSorter.SortColumn)
            {
                lvwColumnSorter.SortColumn = e.Column;
                lvwColumnSorter.Order = SortOrder.Ascending;
            }
            else
                lvwColumnSorter.Order = lvwColumnSorter.Order == SortOrder.Ascending ? SortOrder.Descending : SortOrder.Ascending;

            listViewSmartScripts.Sort();
        }

        private void SortListView(SortOrder order, int column)
        {
            listViewSmartScripts.ListViewItemSorter = lvwColumnSorter;

            if (column != lvwColumnSorter.SortColumn)
            {
                lvwColumnSorter.SortColumn = column;
                lvwColumnSorter.Order = order != SortOrder.None ? order : SortOrder.Ascending;
            }
            else
                lvwColumnSorter.Order = order != SortOrder.None ? order : SortOrder.Ascending;

            listViewSmartScripts.Sort();
        }

        private ListView.ListViewItemCollection GetItemsBasedOnSelection(ListView listView)
        {
            ListView listViewScriptsCopy = new ListView();

            foreach (ListViewItem item in listView.Items)
                if (item.SubItems[1].Text == listView.SelectedItems[0].SubItems[1].Text)
                    listViewScriptsCopy.Items.Add((ListViewItem)item.Clone());

            return listViewScriptsCopy.Items;
        }

        private void buttonLinkTo_Click(object sender, EventArgs e)
        {
            TryToOpenLinkForm(textBoxLinkTo);
        }

        private void buttonLinkFrom_Click(object sender, EventArgs e)
        {
            TryToOpenLinkForm(textBoxLinkFrom);
        }

        private void TryToOpenLinkForm(TextBox textBoxToChange)
        {
            if (listViewSmartScripts.Items.Count <= 1)
            {
                MessageBox.Show("There are not enough items in the listview in order to link!", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (listViewSmartScripts.SelectedItems.Count == 0)
            {
                MessageBox.Show("You must first select a line in the script", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (SearchForLinkForm searchForLinkForm = new SearchForLinkForm(listViewSmartScripts.SmartScripts, listViewSmartScripts.SelectedItems[0].Index, textBoxToChange))
                searchForLinkForm.ShowDialog(this);
        }

        protected override void WndProc(ref Message m)
        {
            //! Don't allow moving the window while we are expanding or contracting. This is required because
            //! the window often breaks and has an incorrect size in the end if the application had been moved
            //! while expanding or contracting.
            if (((m.Msg == 274 && m.WParam.ToInt32() == 61456) || (m.Msg == 161 && m.WParam.ToInt32() == 2)) && (expandingToMainForm || contractingToLoginForm))
                return;

            base.WndProc(ref m);
        }

        private void listViewSmartScripts_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
                if (listViewSmartScripts.FocusedItem.Bounds.Contains(e.Location))
                    contextMenuStripListView.Show(Cursor.Position);
        }

        private void testToolStripMenuItemDeleteRow_Click(object sender, EventArgs e)
        {
            DeleteSelectedRow();
        }

        private void ResetFieldsToDefault(bool withStatic = false)
        {
            if (withStatic)
            {
                textBoxEntryOrGuid.Text = String.Empty;
                comboBoxSourceType.SelectedIndex = 0;
            }

            comboBoxEventType.SelectedIndex = 0;
            comboBoxActionType.SelectedIndex = 0;
            comboBoxTargetType.SelectedIndex = 0;
            textBoxEventType.Text = "0";
            textBoxActionType.Text = "0";
            textBoxTargetType.Text = "0";
            textBoxEventChance.Text = "100";
            textBoxId.Text = "-1";
            textBoxLinkFrom.Text = "None";
            textBoxLinkTo.Text = "0";
            textBoxComments.Text = SAI_Editor_Manager.Instance.GetDefaultCommentForSourceType(GetSourceTypeByIndex());
            textBoxEventPhasemask.Text = "0";
            textBoxEventFlags.Text = "0";

            foreach (TabPage page in tabControlParameters.TabPages)
                foreach (Control control in page.Controls)
                    if (control is TextBox)
                        control.Text = "0";

            SetVisibilityOfAllParamButtons(false);
        }

        private void SetVisibilityOfAllParamButtons(bool visible)
        {
            buttonEventParamOneSearch.Visible = visible;
            buttonEventParamTwoSearch.Visible = visible;
            buttonEventParamThreeSearch.Visible = visible;
            buttonEventParamFourSearch.Visible = visible;
            buttonActionParamOneSearch.Visible = visible;
            buttonActionParamTwoSearch.Visible = visible;
            buttonActionParamThreeSearch.Visible = visible;
            buttonActionParamFourSearch.Visible = visible;
            buttonActionParamFiveSearch.Visible = visible;
            buttonActionParamSixSearch.Visible = visible;
            buttonTargetParamOneSearch.Visible = visible;
            buttonTargetParamTwoSearch.Visible = visible;
            buttonTargetParamThreeSearch.Visible = visible;
            buttonTargetParamFourSearch.Visible = visible;
            buttonTargetParamFiveSearch.Visible = visible;
            buttonTargetParamSixSearch.Visible = visible;
            buttonTargetParamSevenSearch.Visible = visible;
        }

        private void SetVisibilityOfAllEventParamButtons(bool visible)
        {
            buttonEventParamOneSearch.Visible = visible;
            buttonEventParamTwoSearch.Visible = visible;
            buttonEventParamThreeSearch.Visible = visible;
            buttonEventParamFourSearch.Visible = visible;
        }

        private void SetVisibilityOfAllActionParamButtons(bool visible)
        {
            buttonActionParamOneSearch.Visible = visible;
            buttonActionParamTwoSearch.Visible = visible;
            buttonActionParamThreeSearch.Visible = visible;
            buttonActionParamFourSearch.Visible = visible;
            buttonActionParamFiveSearch.Visible = visible;
            buttonActionParamSixSearch.Visible = visible;
        }

        private void SetVisibilityOfAllTargetParamButtons(bool visible)
        {
            buttonTargetParamOneSearch.Visible = visible;
            buttonTargetParamTwoSearch.Visible = visible;
            buttonTargetParamThreeSearch.Visible = visible;
            buttonTargetParamFourSearch.Visible = visible;
            buttonTargetParamFiveSearch.Visible = visible;
            buttonTargetParamSixSearch.Visible = visible;
            buttonTargetParamSevenSearch.Visible = visible;
        }

        private string GetSourceTypeString(SourceTypes sourceType)
        {
            switch (sourceType)
            {
                case SourceTypes.SourceTypeCreature:
                    return "creature";
                case SourceTypes.SourceTypeGameobject:
                    return "gameobject";
                case SourceTypes.SourceTypeAreaTrigger:
                    return "areatrigger";
                case SourceTypes.SourceTypeScriptedActionlist:
                    return "actionlist";
                default:
                    return "unknown";
            }
        }

        private void buttonEventParamOneSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxEventParam1;

            switch ((SmartEvent)comboBoxEventType.SelectedIndex)
            {
                case SmartEvent.SMART_EVENT_SPELLHIT: //! Spell id
                case SmartEvent.SMART_EVENT_FRIENDLY_MISSING_BUFF: //! Spell id
                case SmartEvent.SMART_EVENT_HAS_AURA: //! Spell id
                case SmartEvent.SMART_EVENT_TARGET_BUFFED: //! Spell id
                case SmartEvent.SMART_EVENT_SPELLHIT_TARGET: //! Spell id
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeSpell))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_RESPAWN: //! Respawn condition
                    using (SingleSelectForm<SmartScriptRespawnCondition> singleSelectForm = new SingleSelectForm<SmartScriptRespawnCondition>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_SUMMON_DESPAWNED: //! Creature entry
                case SmartEvent.SMART_EVENT_SUMMONED_UNIT: //! Creature entry
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeCreatureEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_AREATRIGGER_ONTRIGGER: //! Areatrigger entry
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeAreaTrigger))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_GO_STATE_CHANGED: //! Go state
                    using (SingleSelectForm<GoStates> singleSelectForm = new SingleSelectForm<GoStates>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_GAME_EVENT_START: //! Game event entry
                case SmartEvent.SMART_EVENT_GAME_EVENT_END: //! Game event entry
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeGameEvent))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_MOVEMENTINFORM: //! Motion type
                    using (SingleSelectForm<MovementGeneratorType> singleSelectForm = new SingleSelectForm<MovementGeneratorType>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_ACCEPTED_QUEST: //! Quest id
                case SmartEvent.SMART_EVENT_REWARD_QUEST: //! Quest id
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeQuest))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_RECEIVE_EMOTE: //! Emote id
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeEmote))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_GOSSIP_SELECT: //! Gossip menu id
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeGossipMenuOption))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
            }
        }

        private void buttonEventParamTwoSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxEventParam2;

            switch ((SmartEvent)comboBoxEventType.SelectedIndex)
            {
                case SmartEvent.SMART_EVENT_SPELLHIT: //! Spell school
                case SmartEvent.SMART_EVENT_SPELLHIT_TARGET: //! Spell school
                    using (SingleSelectForm<SpellSchools> singleSelectForm = new SingleSelectForm<SpellSchools>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_RESPAWN: //! Map
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeMap))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_TEXT_OVER: //! Creature entry
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeCreatureEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_GOSSIP_SELECT: //! Gossip id
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeGossipOptionId))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
            }
        }

        private void buttonEventParamThreeSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxEventParam3;

            switch ((SmartEvent)comboBoxEventType.SelectedIndex)
            {
                case SmartEvent.SMART_EVENT_RESPAWN: //! Zone
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeZone))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartEvent.SMART_EVENT_VICTIM_CASTING: //! Spell id
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeSpell))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
            }
        }

        private void buttonEventParamFourSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxEventParam4;

            switch ((SmartEvent)comboBoxEventType.SelectedIndex)
            {
                case SmartEvent.SMART_EVENT_KILL: //! Creature entry
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeCreatureEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
            }
        }

        private void buttonTargetParamOneSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxTargetParam1;

            switch ((SmartTarget)comboBoxTargetType.SelectedIndex)
            {
                case SmartTarget.SMART_TARGET_CREATURE_RANGE: //! Creature entry
                case SmartTarget.SMART_TARGET_CREATURE_DISTANCE:
                case SmartTarget.SMART_TARGET_CLOSEST_CREATURE:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeCreatureEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartTarget.SMART_TARGET_CREATURE_GUID: //! Creature guid
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeCreatureGuid))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartTarget.SMART_TARGET_GAMEOBJECT_RANGE:
                case SmartTarget.SMART_TARGET_GAMEOBJECT_DISTANCE:
                case SmartTarget.SMART_TARGET_CLOSEST_GAMEOBJECT: //! Gameobject entry
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeGameobjectEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartTarget.SMART_TARGET_GAMEOBJECT_GUID: //! Gameobject guid
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeGameobjectGuid))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
            }

            switch ((SmartAction)comboBoxActionType.SelectedIndex)
            {
                case SmartAction.SMART_ACTION_INSTALL_AI_TEMPLATE:
                    //! This button is different based on the number in the first parameter field
                    switch ((SmartAiTemplates)XConverter.ToInt32(textBoxActionParam1.Text))
                    {
                        case SmartAiTemplates.SMARTAI_TEMPLATE_CASTER:
                        case SmartAiTemplates.SMARTAI_TEMPLATE_TURRET:
                            using (MultiSelectForm<SmartCastFlags> multiSelectForm = new MultiSelectForm<SmartCastFlags>(textBoxToChange))
                                multiSelectForm.ShowDialog(this);
                            break;
                    }
                    break;
            }
        }

        private void buttonTargetParamTwoSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxTargetParam2;

            switch ((SmartTarget)comboBoxTargetType.SelectedIndex)
            {
                case SmartTarget.SMART_TARGET_CREATURE_GUID: //! Creature entry
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeCreatureEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartTarget.SMART_TARGET_GAMEOBJECT_GUID: //! Gameobject entry
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeGameobjectEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
            }
        }

        private void buttonTargetParamThreeSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxTargetParam3;
        }

        private void buttonTargetParamFourSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxTargetX;
        }

        private void buttonTargetParamFiveSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxTargetY;
        }

        private void buttonTargetParamSixSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxTargetZ;
        }

        private void buttonTargetParamSevenSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxTargetO;
        }

        private void buttonActionParamOneSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxActionParam1;

            switch ((SmartAction)comboBoxActionType.SelectedIndex)
            {
                case SmartAction.SMART_ACTION_CAST:
                case SmartAction.SMART_ACTION_INVOKER_CAST:
                case SmartAction.SMART_ACTION_CROSS_CAST:
                case SmartAction.SMART_ACTION_REMOVEAURASFROMSPELL:
                case SmartAction.SMART_ACTION_ADD_AURA:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeSpell))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SET_FACTION:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeFaction))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_EMOTE:
                case SmartAction.SMART_ACTION_RANDOM_EMOTE:
                case SmartAction.SMART_ACTION_SET_EMOTE_STATE:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeEmote))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_FAIL_QUEST:
                case SmartAction.SMART_ACTION_ADD_QUEST:
                case SmartAction.SMART_ACTION_CALL_AREAEXPLOREDOREVENTHAPPENS:
                case SmartAction.SMART_ACTION_CALL_GROUPEVENTHAPPENS:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeQuest))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SET_REACT_STATE:
                    using (SingleSelectForm<ReactStates> singleSelectForm = new SingleSelectForm<ReactStates>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SOUND:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeSound))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_MORPH_TO_ENTRY_OR_MODEL:
                case SmartAction.SMART_ACTION_SUMMON_CREATURE:
                case SmartAction.SMART_ACTION_CALL_CASTEDCREATUREORGO:
                case SmartAction.SMART_ACTION_KILLED_MONSTER:
                case SmartAction.SMART_ACTION_UPDATE_TEMPLATE:
                case SmartAction.SMART_ACTION_MOUNT_TO_ENTRY_OR_MODEL:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeCreatureEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_GO_SET_LOOT_STATE:
                    using (SingleSelectForm<GoStates> singleSelectForm = new SingleSelectForm<GoStates>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SET_POWER:
                case SmartAction.SMART_ACTION_ADD_POWER:
                case SmartAction.SMART_ACTION_REMOVE_POWER:
                    using (SingleSelectForm<PowerTypes> singleSelectForm = new SingleSelectForm<PowerTypes>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SUMMON_GO:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeGameobjectEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SET_EVENT_PHASE:
                case SmartAction.SMART_ACTION_RANDOM_PHASE:
                case SmartAction.SMART_ACTION_RANDOM_PHASE_RANGE:
                    using (MultiSelectForm<SmartPhaseMasks> multiSelectForm = new MultiSelectForm<SmartPhaseMasks>(textBoxToChange))
                        multiSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SET_PHASE_MASK:
                    using (MultiSelectForm<PhaseMasks> multiSelectForm = new MultiSelectForm<PhaseMasks>(textBoxToChange))
                        multiSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_ADD_ITEM:
                case SmartAction.SMART_ACTION_REMOVE_ITEM:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeItemEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_TELEPORT:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeMap))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SUMMON_CREATURE_GROUP:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeSummonsId))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SET_SHEATH:
                    using (SingleSelectForm<SheathState> singleSelectForm = new SingleSelectForm<SheathState>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_ACTIVATE_TAXI:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeTaxiPath))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SET_UNIT_FLAG:
                case SmartAction.SMART_ACTION_REMOVE_UNIT_FLAG:
                    //! There should be a different form opened based on parameter 2. If parameter two is set to '0' it means
                    //! we target UNIT_FIELD_FLAGS. If it's above 0 it means we target UNIT_FIELD_FLAGS2 (notice the 2).
                    if (textBoxActionParam2.Text == "0" || String.IsNullOrWhiteSpace(textBoxActionParam2.Text))
                    {
                        using (MultiSelectForm<UnitFlags> multiSelectForm = new MultiSelectForm<UnitFlags>(textBoxToChange))
                            multiSelectForm.ShowDialog(this);
                    }
                    else
                    {
                        using (MultiSelectForm<UnitFlags2> multiSelectForm = new MultiSelectForm<UnitFlags2>(textBoxToChange))
                            multiSelectForm.ShowDialog(this);
                    }

                    break;
                case SmartAction.SMART_ACTION_SET_GO_FLAG:
                case SmartAction.SMART_ACTION_ADD_GO_FLAG:
                case SmartAction.SMART_ACTION_REMOVE_GO_FLAG:
                    using (MultiSelectForm<GoFlags> multiSelectForm = new MultiSelectForm<GoFlags>(textBoxToChange))
                        multiSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SET_DYNAMIC_FLAG:
                case SmartAction.SMART_ACTION_ADD_DYNAMIC_FLAG:
                case SmartAction.SMART_ACTION_REMOVE_DYNAMIC_FLAG:
                    using (MultiSelectForm<DynamicFlags> multiSelectForm = new MultiSelectForm<DynamicFlags>(textBoxToChange))
                        multiSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_EQUIP:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeEquipTemplate))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SET_NPC_FLAG:
                case SmartAction.SMART_ACTION_ADD_NPC_FLAG:
                case SmartAction.SMART_ACTION_REMOVE_NPC_FLAG:
                    using (MultiSelectForm<NpcFlags> multiSelectForm = new MultiSelectForm<NpcFlags>(textBoxToChange))
                        multiSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_INSTALL_AI_TEMPLATE:
                    using (SingleSelectForm<SmartAiTemplates> singleSelectForm = new SingleSelectForm<SmartAiTemplates>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);

                    ParameterInstallAiTemplateChanged();
                    break;
                case SmartAction.SMART_ACTION_SET_UNIT_FIELD_BYTES_1:
                case SmartAction.SMART_ACTION_REMOVE_UNIT_FIELD_BYTES_1:
                    int searchType;

                    if (Int32.TryParse(textBoxActionParam2.Text, out searchType))
                    {
                        switch (searchType)
                        {
                            case 0:
                                using (SingleSelectForm<UnitStandStateType> singleSelectForm = new SingleSelectForm<UnitStandStateType>(textBoxToChange))
                                    singleSelectForm.ShowDialog(this);
                                break;
                            //case 1:
                            //    break;
                            case 2:
                                using (MultiSelectForm<UnitStandFlags> multiSelectForm = new MultiSelectForm<UnitStandFlags>(textBoxToChange))
                                    multiSelectForm.ShowDialog(this);
                                break;
                            case 3:
                                using (MultiSelectForm<UnitBytes1_Flags> multiSelectForm = new MultiSelectForm<UnitBytes1_Flags>(textBoxToChange))
                                    multiSelectForm.ShowDialog(this);
                                break;
                            default:
                                MessageBox.Show("The second parameter (type) must be set to a valid search type (0, 2 or 3).", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                break;
                        }
                    }

                    break;
            }
        }

        private void SetTextOfAllEventParameterLabels(string str)
        {
            labelEventParam1.Text = str;
            labelEventParam2.Text = str;
            labelEventParam3.Text = str;
            labelEventParam4.Text = str;
        }

        private void SetTextOfAllActionParameterLabels(string str)
        {
            labelActionParam1.Text = str;
            labelActionParam2.Text = str;
            labelActionParam3.Text = str;
            labelActionParam4.Text = str;
            labelActionParam5.Text = str;
            labelActionParam6.Text = str;
        }

        private void SetTextOfAllTargetParameterLabels(string str)
        {
            labelTargetParam1.Text = str;
            labelTargetParam2.Text = str;
            labelTargetParam3.Text = str;
        }

        private void ParameterInstallAiTemplateChanged()
        {
            SetVisibilityOfAllActionParamButtons(false);
            SetVisibilityOfAllTargetParamButtons(false);
            SetTextOfAllActionParameterLabels("");

            labelActionParam1.Text = "Template entry";
            buttonActionParamOneSearch.Visible = true;
            int newTemplateId = XConverter.ToInt32(textBoxActionParam1.Text);

            switch ((SmartAiTemplates)newTemplateId)
            {
                case SmartAiTemplates.SMARTAI_TEMPLATE_BASIC:
                case SmartAiTemplates.SMARTAI_TEMPLATE_PASSIVE:
                    break;
                case SmartAiTemplates.SMARTAI_TEMPLATE_CASTER:
                case SmartAiTemplates.SMARTAI_TEMPLATE_TURRET:
                    labelActionParam2.Text = "Spell id";
                    buttonActionParamTwoSearch.Visible = true; //! Spell id
                    labelActionParam3.Text = "RepeatMin";
                    labelActionParam4.Text = "RepeatMax";
                    labelActionParam5.Text = "Range";
                    labelActionParam6.Text = "Mana pct";

                    labelTargetParam1.Text = "Castflag";
                    buttonTargetParamOneSearch.Visible = true;
                    break;
                case SmartAiTemplates.SMARTAI_TEMPLATE_CAGED_GO_PART:
                    labelActionParam2.Text = "Creature entry";
                    buttonActionParamTwoSearch.Visible = true; //! Creature entry
                    labelActionParam3.Text = "Credit at end (0/1)";
                    break;
                case SmartAiTemplates.SMARTAI_TEMPLATE_CAGED_NPC_PART:
                    labelActionParam2.Text = "Gameobject entry";
                    buttonActionParamTwoSearch.Visible = true; //! Gameobject entry
                    labelActionParam3.Text = "Despawn time";
                    labelActionParam4.Text = "Walk/run (0/1)";
                    labelActionParam5.Text = "Distance";
                    labelActionParam6.Text = "Group id";
                    break;
            }
        }

        private void buttonActionParamTwoSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxActionParam2;

            switch ((SmartAction)comboBoxActionType.SelectedIndex)
            {
                case SmartAction.SMART_ACTION_CAST:
                case SmartAction.SMART_ACTION_INVOKER_CAST:
                case SmartAction.SMART_ACTION_CROSS_CAST:
                    using (MultiSelectForm<SmartCastFlags> multiSelectForm = new MultiSelectForm<SmartCastFlags>(textBoxToChange))
                        multiSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_WP_STOP:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeQuest))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_INTERRUPT_SPELL:
                case SmartAction.SMART_ACTION_CALL_CASTEDCREATUREORGO:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeSpell))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SUMMON_CREATURE:
                    using (SingleSelectForm<TempSummonType> singleSelectForm = new SingleSelectForm<TempSummonType>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_RANDOM_PHASE:
                case SmartAction.SMART_ACTION_RANDOM_PHASE_RANGE:
                    using (MultiSelectForm<SmartPhaseMasks> multiSelectForm = new MultiSelectForm<SmartPhaseMasks>(textBoxToChange))
                        multiSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_RANDOM_EMOTE:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeEmote))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_INSTALL_AI_TEMPLATE:
                    //! This button is different based on the number in the first parameter field
                    switch ((SmartAiTemplates)XConverter.ToInt32(textBoxActionParam1.Text))
                    {
                        case SmartAiTemplates.SMARTAI_TEMPLATE_CASTER:
                        case SmartAiTemplates.SMARTAI_TEMPLATE_TURRET:
                            using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeSpell))
                                searchFromDatabaseForm.ShowDialog(this);
                            break;
                        case SmartAiTemplates.SMARTAI_TEMPLATE_CAGED_GO_PART:
                            using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeCreatureEntry))
                                searchFromDatabaseForm.ShowDialog(this);
                            break;
                        case SmartAiTemplates.SMARTAI_TEMPLATE_CAGED_NPC_PART:
                            using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeGameobjectEntry))
                                searchFromDatabaseForm.ShowDialog(this);
                            break;
                    }
                    break;
                case SmartAction.SMART_ACTION_WP_START:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeWaypoint))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SEND_GOSSIP_MENU:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeNpcText))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_SET_UNIT_FIELD_BYTES_1:
                case SmartAction.SMART_ACTION_REMOVE_UNIT_FIELD_BYTES_1:
                    using (SingleSelectForm<UnitFieldBytes1Types> singleSelectForm = new SingleSelectForm<UnitFieldBytes1Types>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_CALL_TIMED_ACTIONLIST:
                    using (SingleSelectForm<ActionlistTimerUpdateType> singleSelectForm = new SingleSelectForm<ActionlistTimerUpdateType>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
            }
        }

        private void buttonActionParamThreeSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxActionParam3;

            switch ((SmartAction)comboBoxActionType.SelectedIndex)
            {
                case SmartAction.SMART_ACTION_FOLLOW:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeCreatureEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_CROSS_CAST:
                    using (SingleSelectForm<SmartTarget> singleSelectForm = new SingleSelectForm<SmartTarget>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_RANDOM_PHASE:
                    using (MultiSelectForm<SmartPhaseMasks> multiSelectForm = new MultiSelectForm<SmartPhaseMasks>(textBoxToChange))
                        multiSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_EQUIP:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeItemEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_RANDOM_EMOTE:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeEmote))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
            }
        }

        private void buttonActionParamFourSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxActionParam4;

            switch ((SmartAction)comboBoxActionType.SelectedIndex)
            {
                case SmartAction.SMART_ACTION_FOLLOW:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeCreatureEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_WP_START:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeQuest))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_RANDOM_PHASE:
                    using (MultiSelectForm<SmartPhaseMasks> multiSelectForm = new MultiSelectForm<SmartPhaseMasks>(textBoxToChange))
                        multiSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_EQUIP:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeItemEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_RANDOM_EMOTE:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeEmote))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
            }
        }

        private void buttonActionParamFiveSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxActionParam5;

            switch ((SmartAction)comboBoxActionType.SelectedIndex)
            {
                case SmartAction.SMART_ACTION_RANDOM_PHASE:
                    using (MultiSelectForm<SmartPhaseMasks> multiSelectForm = new MultiSelectForm<SmartPhaseMasks>(textBoxToChange))
                        multiSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_EQUIP:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeItemEntry))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_RANDOM_EMOTE:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeEmote))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
            }
        }

        private void buttonActionParamSixSearch_Click(object sender, EventArgs e)
        {
            TextBox textBoxToChange = textBoxActionParam6;

            switch ((SmartAction)comboBoxActionType.SelectedIndex)
            {
                case SmartAction.SMART_ACTION_WP_START:
                    using (SingleSelectForm<ReactStates> singleSelectForm = new SingleSelectForm<ReactStates>(textBoxToChange))
                        singleSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_RANDOM_PHASE:
                    using (MultiSelectForm<SmartPhaseMasks> multiSelectForm = new MultiSelectForm<SmartPhaseMasks>(textBoxToChange))
                        multiSelectForm.ShowDialog(this);
                    break;
                case SmartAction.SMART_ACTION_RANDOM_EMOTE:
                    using (SearchFromDatabaseForm searchFromDatabaseForm = new SearchFromDatabaseForm(connectionString, textBoxToChange, DatabaseSearchFormType.DatabaseSearchFormTypeEmote))
                        searchFromDatabaseForm.ShowDialog(this);
                    break;
            }
        }

        private void TryToOpenPage(string url)
        {
            try
            {
                Process.Start(url);
            }
            catch (Exception)
            {
                MessageBox.Show("The webpage could not be opened!", "An error has occurred!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void smartAIWikiToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TryToOpenPage("http://collab.kpsn.org/display/tc/smart_scripts");
        }

        private void textBoxComments_GotFocus(object sender, EventArgs e)
        {
            if (textBoxComments.Text == SAI_Editor_Manager.Instance.GetDefaultCommentForSourceType(GetSourceTypeByIndex()))
                textBoxComments.Text = "";
        }

        private void textBoxComments_LostFocus(object sender, EventArgs e)
        {
            if (String.IsNullOrEmpty(textBoxComments.Text))
                textBoxComments.Text = SAI_Editor_Manager.Instance.GetDefaultCommentForSourceType(GetSourceTypeByIndex());
        }

        public void ExpandToShowPermanentTooltips(bool expand)
        {
            listViewSmartScriptsInitialHeight = listViewSmartScripts.Height;
            expandingListView = expand;
            contractingListView = !expand;
            listViewSmartScriptsHeightToChangeTo = expand ? listViewSmartScripts.Height + (int)FormSizes.ListViewHeightContract : listViewSmartScripts.Height - (int)FormSizes.ListViewHeightContract;
            timerShowPermanentTooltips.Enabled = true;
            ToolTipHelper.DisableOrEnableAllToolTips(false);

            if (expand)
            {
                panelPermanentTooltipTypes.Visible = false;
                panelPermanentTooltipParameters.Visible = false;
                listViewSmartScriptsHeightToChangeTo = listViewSmartScripts.Height + (int)FormSizes.ListViewHeightContract;
                ChangeParameterFieldsBasedOnType();
            }
            else
            {
                listViewSmartScriptsHeightToChangeTo = listViewSmartScripts.Height - (int)FormSizes.ListViewHeightContract;
                //ChangeParameterFieldsBasedOnType();
            }
        }

        private void timerShowPermanentTooltips_Tick(object sender, EventArgs e)
        {
            if (expandingListView)
            {
                if (listViewSmartScripts.Height < listViewSmartScriptsHeightToChangeTo)
                    listViewSmartScripts.Height += expandAndContractSpeedListView;
                else
                {
                    listViewSmartScripts.Height = listViewSmartScriptsHeightToChangeTo;
                    timerShowPermanentTooltips.Enabled = false;
                    expandingListView = false;
                    ToolTipHelper.DisableOrEnableAllToolTips(true);
                    checkBoxUsePermanentTooltips.Enabled = true;
                }
            }
            else if (contractingListView)
            {
                if (listViewSmartScripts.Height > listViewSmartScriptsHeightToChangeTo)
                    listViewSmartScripts.Height -= expandAndContractSpeedListView;
                else
                {
                    listViewSmartScripts.Height = listViewSmartScriptsHeightToChangeTo;
                    timerShowPermanentTooltips.Enabled = false;
                    contractingListView = false;
                    panelPermanentTooltipTypes.Visible = true;
                    panelPermanentTooltipParameters.Visible = true;
                    checkBoxUsePermanentTooltips.Enabled = true;
                }
            }
        }

        private void comboBoxEventType_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfTypes(sender as ComboBox, ScriptTypeId.ScriptTypeEvent);
        }

        private void comboBoxActionType_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfTypes(sender as ComboBox, ScriptTypeId.ScriptTypeAction);
        }

        private void comboBoxTargetType_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfTypes(sender as ComboBox, ScriptTypeId.ScriptTypeTarget);
        }

        private void UpdatePermanentTooltipOfTypes(ComboBox comboBoxToTarget, ScriptTypeId scriptTypeId)
        {
            string toolTipOfType = SAI_Editor_Manager.Instance.GetScriptTypeTooltipById(comboBoxToTarget.SelectedIndex, scriptTypeId);
            string toolTipTitleOfType = comboBoxToTarget.SelectedItem.ToString();

            if (!String.IsNullOrWhiteSpace(toolTipOfType) && !String.IsNullOrWhiteSpace(toolTipTitleOfType))
            {
                labelPermanentTooltipTextTypes.Text = toolTipOfType;
                labelPermanentTooltipTitleTypes.Text = toolTipTitleOfType;
            }
        }

        private void UpdatePermanentTooltipOfParameter(Label labelToTarget, int paramId, ComboBox comboBoxToTarget, ScriptTypeId scriptTypeId)
        {
            string toolTipOfType = SAI_Editor_Manager.Instance.GetParameterTooltipById(comboBoxToTarget.SelectedIndex, paramId, scriptTypeId);

            if (!String.IsNullOrWhiteSpace(toolTipOfType))
            {
                labelPermanentTooltipTextParameters.Text = toolTipOfType;
                labelPermanentTooltipParameterTitleTypes.Text = comboBoxToTarget.SelectedItem + " - " + labelToTarget.Text;
            }
        }

        private int GetSelectedIndexByScriptTypeId(ScriptTypeId scriptTypeId)
        {
            switch (scriptTypeId)
            {
                case ScriptTypeId.ScriptTypeEvent:
                    return comboBoxEventType.SelectedIndex;
                case ScriptTypeId.ScriptTypeAction:
                    return comboBoxActionType.SelectedIndex;
                case ScriptTypeId.ScriptTypeTarget:
                    return comboBoxTargetType.SelectedIndex;
            }

            return 0;
        }

        private string GetSelectedItemByScriptTypeId(ScriptTypeId scriptTypeId)
        {
            switch (scriptTypeId)
            {
                case ScriptTypeId.ScriptTypeEvent:
                    return comboBoxEventType.SelectedItem.ToString();
                case ScriptTypeId.ScriptTypeAction:
                    return comboBoxActionType.SelectedItem.ToString();
                case ScriptTypeId.ScriptTypeTarget:
                    return comboBoxTargetType.SelectedItem.ToString();
            }

            return String.Empty;
        }

        private void labelEventParam1_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 1, comboBoxEventType, ScriptTypeId.ScriptTypeEvent);
        }

        private void labelEventParam2_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 2, comboBoxEventType, ScriptTypeId.ScriptTypeEvent);
        }

        private void labelEventParam3_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 3, comboBoxEventType, ScriptTypeId.ScriptTypeEvent);
        }

        private void labelEventParam4_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 4, comboBoxEventType, ScriptTypeId.ScriptTypeEvent);
        }

        private void labelActionParam1_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 1, comboBoxActionType, ScriptTypeId.ScriptTypeAction);
        }

        private void labelActionParam2_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 2, comboBoxActionType, ScriptTypeId.ScriptTypeAction);
        }

        private void labelActionParam3_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 3, comboBoxActionType, ScriptTypeId.ScriptTypeAction);
        }

        private void labelActionParam4_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 4, comboBoxActionType, ScriptTypeId.ScriptTypeAction);
        }

        private void labelActionParam5_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 5, comboBoxActionType, ScriptTypeId.ScriptTypeAction);
        }

        private void labelActionParam6_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 6, comboBoxActionType, ScriptTypeId.ScriptTypeAction);
        }

        private void labelTargetParam1_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 1, comboBoxTargetType, ScriptTypeId.ScriptTypeTarget);
        }

        private void labelTargetParam2_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 2, comboBoxTargetType, ScriptTypeId.ScriptTypeTarget);
        }

        private void labelTargetParam3_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 3, comboBoxTargetType, ScriptTypeId.ScriptTypeTarget);
        }

        private void labelTargetX_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 4, comboBoxTargetType, ScriptTypeId.ScriptTypeTarget);
        }

        private void labelTargetY_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 5, comboBoxTargetType, ScriptTypeId.ScriptTypeTarget);
        }

        private void labelTargetZ_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 6, comboBoxTargetType, ScriptTypeId.ScriptTypeTarget);
        }

        private void labelTargetO_MouseEnter(object sender, EventArgs e)
        {
            if (Settings.Default.ShowTooltipsPermanently)
                UpdatePermanentTooltipOfParameter(sender as Label, 7, comboBoxTargetType, ScriptTypeId.ScriptTypeTarget);
        }

        private async void buttonNewLine_Click(object sender, EventArgs e)
        {
            if (listViewSmartScripts.Items.Count == 0)
            {
                if (!Settings.Default.UseWorldDatabase)
                {
                    TryToCreateScript(true);
                    return;
                }

                string aiName = await SAI_Editor_Manager.Instance.worldDatabase.GetObjectAiName(XConverter.ToInt32(textBoxEntryOrGuid.Text), (int)GetSourceTypeByIndex());

                if (!SAI_Editor_Manager.Instance.IsAiNameSmartAi(aiName))
                {
                    DialogResult dialogResult = MessageBox.Show("Are you sure you want to create a new script for the given entry and sourcetype?", "Something went wrong", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

                    if (dialogResult == DialogResult.Yes)
                        TryToCreateScript(true);
                }
                else
                    TryToCreateScript(true);

                return;
            }

            buttonNewLine.Enabled = false;
            SmartScript newSmartScript = new SmartScript();
            newSmartScript.entryorguid = originalEntryOrGuidAndSourceType.entryOrGuid;
            newSmartScript.source_type = (int)originalEntryOrGuidAndSourceType.sourceType;

            if (checkBoxLockEventId.Checked)
                newSmartScript.id = ++lastSmartScriptIdOfScript;
            else
                newSmartScript.id = -1;

            if (Settings.Default.GenerateComments && Settings.Default.UseWorldDatabase)
                newSmartScript.comment = await CommentGenerator.Instance.GenerateCommentFor(newSmartScript, originalEntryOrGuidAndSourceType);
            else
                newSmartScript.comment = SAI_Editor_Manager.Instance.GetDefaultCommentForSourceType((SourceTypes)newSmartScript.source_type);

            newSmartScript.event_chance = 100;
            int index = listViewSmartScripts.AddSmartScript(newSmartScript);
            HandleShowBasicInfo();

            listViewSmartScripts.Items[index].Selected = true;
            listViewSmartScripts.Select();
            listViewSmartScripts.EnsureVisible(index);

            buttonNewLine.Enabled = textBoxEntryOrGuid.Text.Length > 0;
        }

        private async void textBoxLinkTo_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                if (!updatingFieldsBasedOnSelectedScript && listViewSmartScripts.SelectedSmartScript.id.ToString() == textBoxLinkTo.Text)
                {
                    MessageBox.Show("You can not link to or from the same id you're linking to.", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    textBoxLinkFrom.Text = GetLinkFromForSelection();
                    textBoxLinkTo.Text = "0";
                    return;
                }

                int linkTo = XConverter.ToInt32(textBoxLinkTo.Text);
                listViewSmartScripts.SelectedSmartScript.link = linkTo;
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);

                foreach (SmartScript smartScript in listViewSmartScripts.SmartScripts)
                {
                    if (smartScript.id == linkTo)
                    {
                        if ((SmartEvent)smartScript.event_type == SmartEvent.SMART_EVENT_LINK)
                            await GenerateCommentForSmartScript(smartScript, false);

                        break;
                    }
                }
            }
        }

        private void textBoxComments_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.comment = textBoxComments.Text;
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                ResizeColumns();
            }
        }

        private async void textBoxEventPhasemask_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.event_phase_mask = XConverter.ToInt32(textBoxEventPhasemask.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxEventChance_ValueChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.event_chance = (int)textBoxEventChance.Value; //! Using .Text propert results in wrong value
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxEventFlags_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.event_flags = XConverter.ToInt32(textBoxEventFlags.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxLinkFrom_TextChanged(object sender, EventArgs e)
        {
            int newLinkFrom = 0;// XConverter.ToInt32(textBoxLinkFrom.Text);

            try
            {
                newLinkFrom = Int32.Parse(textBoxLinkFrom.Text);
            }
            catch (Exception)
            {
                previousLinkFrom = -1;
                return;
            }

            //! Only if the property was changed by hand (by user) and not by selecting a line
            if (!updatingFieldsBasedOnSelectedScript)
            {
                if (newLinkFrom == listViewSmartScripts.SelectedSmartScript.id)
                {
                    MessageBox.Show("You can not link to or from the same id you're linking to.", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    textBoxLinkFrom.Text = GetLinkFromForSelection();
                    previousLinkFrom = -1;
                    return;
                }

                if (previousLinkFrom == newLinkFrom)
                    return;

                for (int i = 0; i < listViewSmartScripts.SmartScripts.Count; ++i)
                {
                    SmartScript smartScript = listViewSmartScripts.SmartScripts[i];

                    if (smartScript.entryorguid != originalEntryOrGuidAndSourceType.entryOrGuid || smartScript.source_type != (int)originalEntryOrGuidAndSourceType.sourceType)
                        continue;

                    if (smartScript.link == previousLinkFrom)
                    {
                        smartScript.link = 0;
                        await GenerateCommentForSmartScript(smartScript, false);
                    }

                    if (smartScript.id == newLinkFrom && listViewSmartScripts.SelectedSmartScript != null)
                    {
                        smartScript.link = listViewSmartScripts.SelectedSmartScript.id;
                        await GenerateCommentForSmartScript(smartScript, false);
                    }
                }

                listViewSmartScripts.Init(true);
            }

            previousLinkFrom = newLinkFrom;
        }

        private async void textBoxEventParam1_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.event_param1 = XConverter.ToInt32(textBoxEventParam1.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxEventParam2_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.event_param2 = XConverter.ToInt32(textBoxEventParam2.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxEventParam3_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.event_param3 = XConverter.ToInt32(textBoxEventParam3.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxEventParam4_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.event_param4 = XConverter.ToInt32(textBoxEventParam4.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxActionParam1_TextChanged(object sender, EventArgs e)
        {
            if ((SmartAction)comboBoxActionType.SelectedIndex == SmartAction.SMART_ACTION_INSTALL_AI_TEMPLATE)
                ParameterInstallAiTemplateChanged();

            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.action_param1 = XConverter.ToInt32(textBoxActionParam1.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxActionParam2_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.action_param2 = XConverter.ToInt32(textBoxActionParam2.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxActionParam3_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.action_param3 = XConverter.ToInt32(textBoxActionParam3.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxActionParam4_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.action_param4 = XConverter.ToInt32(textBoxActionParam4.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxActionParam5_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.action_param5 = XConverter.ToInt32(textBoxActionParam5.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxActionParam6_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.action_param6 = XConverter.ToInt32(textBoxActionParam6.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxTargetParam1_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.target_param1 = XConverter.ToInt32(textBoxTargetParam1.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxTargetParam2_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.target_param2 = XConverter.ToInt32(textBoxTargetParam2.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxTargetParam3_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.target_param3 = XConverter.ToInt32(textBoxTargetParam3.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxTargetX_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                textBoxTargetX.Text = textBoxTargetX.Text.Replace(",", ".");
                textBoxTargetX.SelectionStart = textBoxTargetX.Text.Length + 1; //! Set cursor to end of text
                listViewSmartScripts.SelectedSmartScript.target_x = XConverter.ToDouble(textBoxTargetX.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxTargetY_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                textBoxTargetY.Text = textBoxTargetY.Text.Replace(",", ".");
                textBoxTargetY.SelectionStart = textBoxTargetY.Text.Length + 1; //! Set cursor to end of text
                listViewSmartScripts.SelectedSmartScript.target_y = XConverter.ToDouble(textBoxTargetY.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxTargetZ_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                textBoxTargetZ.Text = textBoxTargetZ.Text.Replace(",", ".");
                textBoxTargetZ.SelectionStart = textBoxTargetZ.Text.Length + 1; //! Set cursor to end of text
                listViewSmartScripts.SelectedSmartScript.target_z = XConverter.ToDouble(textBoxTargetZ.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxTargetO_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                textBoxTargetO.Text = textBoxTargetO.Text.Replace(",", ".");
                textBoxTargetO.SelectionStart = textBoxTargetO.Text.Length + 1; //! Set cursor to end of text
                listViewSmartScripts.SelectedSmartScript.target_o = XConverter.ToDouble(textBoxTargetO.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private async void textBoxEntryOrGuid_TextChanged(object sender, EventArgs e)
        {
            pictureBoxLoadScript.Enabled = textBoxEntryOrGuid.Text.Length > 0 && Settings.Default.UseWorldDatabase;
            pictureBoxCreateScript.Enabled = textBoxEntryOrGuid.Text.Length > 0;
            buttonNewLine.Enabled = textBoxEntryOrGuid.Text.Length > 0;

            if (checkBoxAllowChangingEntryAndSourceType.Checked && listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.entryorguid = XConverter.ToInt32(textBoxEntryOrGuid.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);

                //! When all entryorguids are the same, also adjust the originalEntryOrGuid data
                List<EntryOrGuidAndSourceType> uniqueEntriesOrGuidsAndSourceTypes = SAI_Editor_Manager.Instance.GetUniqueEntriesOrGuidsAndSourceTypes(listViewSmartScripts.SmartScripts);

                if (uniqueEntriesOrGuidsAndSourceTypes != null && uniqueEntriesOrGuidsAndSourceTypes.Count == 1)
                {
                    originalEntryOrGuidAndSourceType.entryOrGuid = uniqueEntriesOrGuidsAndSourceTypes[0].entryOrGuid;
                    originalEntryOrGuidAndSourceType.sourceType = uniqueEntriesOrGuidsAndSourceTypes[0].sourceType;
                }
            }
        }

        private async void comboBoxSourceType_SelectedIndexChanged(object sender, EventArgs e)
        {
            SourceTypes newSourceType = GetSourceTypeByIndex();

            if (listViewSmartScripts.Items.Count == 0)
                textBoxComments.Text = SAI_Editor_Manager.Instance.GetDefaultCommentForSourceType(newSourceType);

            if (checkBoxAllowChangingEntryAndSourceType.Checked && listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.source_type = (int)newSourceType;
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }

            //! When no database connection can be made, only enable the search button if
            //! we're searching for areatriggers.
            buttonSearchForEntryOrGuid.Enabled = Settings.Default.UseWorldDatabase || newSourceType == SourceTypes.SourceTypeAreaTrigger;
        }

        private async void generateSQLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (formState != FormState.FormStateMain)
                return;

            using (SqlOutputForm sqlOutputForm = new SqlOutputForm(await GenerateSmartAiSqlFromListView(), await GenerateSmartAiRevertQuery()))
                sqlOutputForm.ShowDialog(this);
        }

        private async void buttonGenerateSql_Click(object sender, EventArgs e)
        {
            if (formState != FormState.FormStateMain)
                return;

            using (SqlOutputForm sqlOutputForm = new SqlOutputForm(await GenerateSmartAiSqlFromListView(), await GenerateSmartAiRevertQuery()))
                sqlOutputForm.ShowDialog(this);
        }

        private async Task<string> GenerateSmartAiSqlFromListView()
        {
            List<EntryOrGuidAndSourceType> entriesOrGuidsAndSourceTypes = SAI_Editor_Manager.Instance.GetUniqueEntriesOrGuidsAndSourceTypes(listViewSmartScripts.SmartScripts);
            string generatedSql = String.Empty, sourceName = String.Empty;

            Dictionary<SourceTypes, List<EntryOrGuidAndSourceType>> entriesOrGuidsAndSourceTypesPerSourceType = new Dictionary<SourceTypes, List<EntryOrGuidAndSourceType>>();

            if (entriesOrGuidsAndSourceTypes.Count > 1)
            {
                foreach (EntryOrGuidAndSourceType entryOrGuidAndSourceType in entriesOrGuidsAndSourceTypes)
                {
                    if (!entriesOrGuidsAndSourceTypesPerSourceType.ContainsKey(entryOrGuidAndSourceType.sourceType))
                    {
                        List<EntryOrGuidAndSourceType> _newEntryOrGuidAndSourceType = new List<EntryOrGuidAndSourceType>();
                        _newEntryOrGuidAndSourceType.Add(entryOrGuidAndSourceType);
                        entriesOrGuidsAndSourceTypesPerSourceType[entryOrGuidAndSourceType.sourceType] = _newEntryOrGuidAndSourceType;
                    }
                    else
                        entriesOrGuidsAndSourceTypesPerSourceType[entryOrGuidAndSourceType.sourceType].Add(entryOrGuidAndSourceType);
                }
            }

            switch (originalEntryOrGuidAndSourceType.sourceType)
            {
                case SourceTypes.SourceTypeCreature:
                case SourceTypes.SourceTypeGameobject:
                    if (!Settings.Default.UseWorldDatabase)
                    {
                        sourceName = " <Could not generate name>";
                        break;
                    }

                    sourceName = " " + await SAI_Editor_Manager.Instance.worldDatabase.GetObjectNameByIdOrGuidAndSourceType(originalEntryOrGuidAndSourceType.sourceType, originalEntryOrGuidAndSourceType.entryOrGuid);
                    break;
                case SourceTypes.SourceTypeAreaTrigger:
                    sourceName = " Areatrigger";
                    break;
                case SourceTypes.SourceTypeScriptedActionlist:
                    if (entriesOrGuidsAndSourceTypes.Count > 1)
                    {
                        if (!Settings.Default.UseWorldDatabase)
                        {
                            sourceName = " <Could not generate name>";
                            break;
                        }

                        foreach (List<EntryOrGuidAndSourceType> listEntryOrGuidAndSourceTypes in entriesOrGuidsAndSourceTypesPerSourceType.Values)
                        {
                            foreach (EntryOrGuidAndSourceType entryOrGuidAndSourceType in listEntryOrGuidAndSourceTypes)
                            {
                                if (entryOrGuidAndSourceType.sourceType != SourceTypes.SourceTypeGameobject && entryOrGuidAndSourceType.sourceType != SourceTypes.SourceTypeCreature)
                                    continue;

                                sourceName = " " + await SAI_Editor_Manager.Instance.worldDatabase.GetObjectNameByIdOrGuidAndSourceType(entryOrGuidAndSourceType.sourceType, entryOrGuidAndSourceType.entryOrGuid);
                                break;
                            }
                        }
                    }
                    else
                        sourceName = " Actionlist";

                    break;
                default:
                    sourceName = " <Could not generate name>";
                    break;
            }

            bool originalEntryIsGuid = originalEntryOrGuidAndSourceType.entryOrGuid < 0;
            string sourceSet = originalEntryIsGuid ? "@GUID" : "@ENTRY";

            generatedSql += "--" + sourceName + " SAI\n";
            generatedSql += "SET " + sourceSet + " := " + originalEntryOrGuidAndSourceType.entryOrGuid + ";\n";

            if (entriesOrGuidsAndSourceTypes.Count == 1)
            {
                switch ((SourceTypes)originalEntryOrGuidAndSourceType.sourceType)
                {
                    case SourceTypes.SourceTypeCreature:
                        if (!Settings.Default.UseWorldDatabase)
                        {
                            generatedSql += "-- No changes to the AIName were made as there is no world database connection.\n";
                            break;
                        }

                        if (originalEntryIsGuid)
                        {
                            int actualEntry = await SAI_Editor_Manager.Instance.worldDatabase.GetCreatureIdByGuid(-originalEntryOrGuidAndSourceType.entryOrGuid);
                            generatedSql += "UPDATE `creature_template` SET `AIName`=" + '"' + "SmartAI" + '"' + " WHERE `entry`=" + actualEntry + ";\n";
                        }
                        else
                            generatedSql += "UPDATE `creature_template` SET `AIName`=" + '"' + "SmartAI" + '"' + " WHERE `entry`=" + sourceSet + ";\n";

                        break;
                    case SourceTypes.SourceTypeGameobject:
                        if (!Settings.Default.UseWorldDatabase)
                        {
                            generatedSql += "-- No changes to the AIName were made as there is no world database connection.\n";
                            break;
                        }

                        if (originalEntryIsGuid)
                        {
                            int actualEntry = await SAI_Editor_Manager.Instance.worldDatabase.GetGameobjectIdByGuid(-originalEntryOrGuidAndSourceType.entryOrGuid);
                            generatedSql += "UPDATE `gameobject_template` SET `AIName`=" + '"' + "SmartGameObjectAI" + '"' + " WHERE `entry`=" + actualEntry + ";\n";
                        }
                        else
                            generatedSql += "UPDATE `gameobject_template` SET `AIName`=" + '"' + "SmartGameObjectAI" + '"' + " WHERE `entry`=" + sourceSet + ";\n";

                        break;
                    case SourceTypes.SourceTypeAreaTrigger:
                        generatedSql += "DELETE FROM `areatrigger_scripts` WHERE `entry`=" + sourceSet + ";\n";
                        generatedSql += "INSERT INTO `areatrigger_scripts` (`entry`,`ScriptName`) VALUES (" + sourceSet + "," + '"' + "SmartTrigger" + '"' + ");\n";
                        break;
                    case SourceTypes.SourceTypeScriptedActionlist:
                        // todo
                        break;
                }

                generatedSql += "DELETE FROM `smart_scripts` WHERE `entryorguid`=" + sourceSet + " AND `source_type`=" + (int)originalEntryOrGuidAndSourceType.sourceType + ";\n";
            }
            else if (entriesOrGuidsAndSourceTypes.Count > 1)
            {
                foreach (List<EntryOrGuidAndSourceType> listEntryOrGuidAndSourceTypes in entriesOrGuidsAndSourceTypesPerSourceType.Values)
                {
                    bool generatedInitialUpdateQuery = false;

                    for (int i = 0; i < listEntryOrGuidAndSourceTypes.Count; ++i)
                    {
                        EntryOrGuidAndSourceType entryOrGuidAndSourceType = listEntryOrGuidAndSourceTypes[i];

                        switch (entryOrGuidAndSourceType.sourceType)
                        {
                            case SourceTypes.SourceTypeCreature:
                            case SourceTypes.SourceTypeGameobject:
                                string entryOrGuidToUse = entryOrGuidAndSourceType.entryOrGuid.ToString();
                                bool sourceTypeIsCreature = entryOrGuidAndSourceType.sourceType == SourceTypes.SourceTypeCreature;
                                string tableToTarget = sourceTypeIsCreature ? "creature_template" : "gameobject_template";
                                string newAiName = sourceTypeIsCreature ? "SmartAI" : "SmartGameObjectAI";

                                if (entryOrGuidAndSourceType.entryOrGuid == originalEntryOrGuidAndSourceType.entryOrGuid)
                                    entryOrGuidToUse = sourceSet;

                                if (entryOrGuidAndSourceType.entryOrGuid < 0)
                                {
                                    if (!Settings.Default.UseWorldDatabase)
                                    {
                                        generatedSql += "-- No changes to the AIName were made as there is no world database connection.";
                                        break;
                                    }

                                    entryOrGuidToUse = (await SAI_Editor_Manager.Instance.worldDatabase.GetObjectIdByGuidAndSourceType(-entryOrGuidAndSourceType.entryOrGuid, (int)entryOrGuidAndSourceType.sourceType)).ToString();

                                    if (entryOrGuidToUse == "0")
                                    {
                                        string sourceTypeString = GetSourceTypeString(entryOrGuidAndSourceType.sourceType);
                                        string message = "While generating a script for your SmartAI, the " + sourceTypeString + " guid ";
                                        message += -entryOrGuidAndSourceType.entryOrGuid + " was not spawned in your current database which means the AIName was not properly set.";
                                        message += "\n\nThis is only a warning, which means the AIName of entry 0 will be set in `" + sourceTypeString + "_template` and this has no effect.";
                                        MessageBox.Show(message, "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                    }
                                }

                                if (listEntryOrGuidAndSourceTypes.Count > 1)
                                {
                                    if (!generatedInitialUpdateQuery)
                                    {
                                        generatedSql += "UPDATE `" + tableToTarget + "` SET `AIName`=" + '"' + newAiName + '"' + " WHERE `entry` IN (" + entryOrGuidToUse;
                                        generatedInitialUpdateQuery = true;
                                    }
                                    else
                                        generatedSql += "," + entryOrGuidToUse;

                                    if (i == listEntryOrGuidAndSourceTypes.Count - 1)
                                        generatedSql += ");\n";
                                }
                                else
                                    generatedSql += "UPDATE `" + tableToTarget + "` SET `AIName`=" + '"' + newAiName + '"' + " WHERE `entry`=" + entryOrGuidToUse + ";\n";

                                break;
                            case SourceTypes.SourceTypeAreaTrigger:
                                generatedSql += "DELETE FROM `areatrigger_scripts` WHERE `entry`=" + sourceSet + ";\n";
                                generatedSql += "INSERT INTO `areatrigger_scripts` VALUES (" + sourceSet + "," + '"' + "SmartTrigger" + '"' + ");\n";
                                break;
                            case SourceTypes.SourceTypeScriptedActionlist:
                                // todo
                                break;
                        }
                    }
                }

                foreach (List<EntryOrGuidAndSourceType> listEntryOrGuidAndSourceTypes in entriesOrGuidsAndSourceTypesPerSourceType.Values)
                {
                    //! If there are more entries
                    if (listEntryOrGuidAndSourceTypes.Count > 1)
                    {
                        generatedSql += "DELETE FROM `smart_scripts` WHERE `entryorguid` IN (";
                        SourceTypes sourceTypeOfSource = SourceTypes.SourceTypeNone;

                        for (int i = 0; i < listEntryOrGuidAndSourceTypes.Count; ++i)
                        {
                            EntryOrGuidAndSourceType entryOrGuidAndSourceType = listEntryOrGuidAndSourceTypes[i];
                            sourceTypeOfSource = entryOrGuidAndSourceType.sourceType;

                            if (entryOrGuidAndSourceType.entryOrGuid == originalEntryOrGuidAndSourceType.entryOrGuid)
                                generatedSql += sourceSet;
                            else
                                generatedSql += entryOrGuidAndSourceType.entryOrGuid;

                            if (i != listEntryOrGuidAndSourceTypes.Count - 1)
                                generatedSql += ",";
                            else
                                generatedSql += ")";
                        }

                        generatedSql += " AND `source_type`=" + (int)sourceTypeOfSource + ";\n";
                    }
                    else if (listEntryOrGuidAndSourceTypes.Count == 1)
                    {
                        generatedSql += "DELETE FROM `smart_scripts` WHERE `entryorguid`=";

                        if (listEntryOrGuidAndSourceTypes[0].entryOrGuid == originalEntryOrGuidAndSourceType.entryOrGuid)
                            generatedSql += sourceSet;
                        else
                            generatedSql += listEntryOrGuidAndSourceTypes[0].entryOrGuid;

                        generatedSql += " AND `source_type`=" + (int)listEntryOrGuidAndSourceTypes[0].sourceType + ";\n";
                    }
                    else
                        generatedSql += "-- No 'DELETE FROM `smart_scripts` WHERE ...' query could be generated as the size of listEntryOrGuidAndSourceTypes is not correct (" + listEntryOrGuidAndSourceTypes.Count + ").";
                }
            }

            generatedSql += "INSERT INTO `smart_scripts` (`entryorguid`,`source_type`,`id`,`link`,`event_type`,`event_phase_mask`,`event_chance`,`event_flags`,`event_param1`,`event_param2`,`event_param3`,`event_param4`,`action_type`,`action_param1`,`action_param2`,`action_param3`,`action_param4`,`action_param5`,`action_param6`,`target_type`,`target_param1`,`target_param2`,`target_param3`,`target_x`,`target_y`,`target_z`,`target_o`,`comment`) VALUES\n";

            List<SmartScript> smartScripts = listViewSmartScripts.SmartScripts;
            smartScripts = smartScripts.OrderBy(smartScript => smartScript.entryorguid).ToList();

            for (int i = 0; i < smartScripts.Count; ++i)
            {
                SmartScript smartScript = smartScripts[i];
                string actualSourceSet = sourceSet;

                if (originalEntryOrGuidAndSourceType.entryOrGuid != smartScripts[i].entryorguid)
                    actualSourceSet = smartScripts[i].entryorguid.ToString();

                int[] eventParameters = new int[4];
                eventParameters[0] = smartScript.event_param1;
                eventParameters[1] = smartScript.event_param2;
                eventParameters[2] = smartScript.event_param3;
                eventParameters[3] = smartScript.event_param4;

                int[] actionParameters = new int[6];
                actionParameters[0] = smartScript.action_param1;
                actionParameters[1] = smartScript.action_param2;
                actionParameters[2] = smartScript.action_param3;
                actionParameters[3] = smartScript.action_param4;
                actionParameters[4] = smartScript.action_param5;
                actionParameters[5] = smartScript.action_param6;

                int[] targetParameters = new int[3];
                targetParameters[0] = smartScript.target_param1;
                targetParameters[1] = smartScript.target_param2;
                targetParameters[2] = smartScript.target_param3;

                for (int x = 0; x < 6; ++x)
                {
                    if (x < 4)
                        if (eventParameters[x].ToString() == sourceSet)
                            eventParameters[x] = XConverter.ToInt32(sourceSet);

                    if (actionParameters[x].ToString() == sourceSet)
                        actionParameters[x] = XConverter.ToInt32(sourceSet);

                    if (x < 3)
                        if (targetParameters[x].ToString() == sourceSet)
                            targetParameters[x] = XConverter.ToInt32(sourceSet);
                }

                generatedSql += "(" + actualSourceSet + "," + smartScript.source_type + "," + smartScript.id + "," + smartScript.link + "," + smartScript.event_type + "," +
                                              smartScript.event_phase_mask + "," + smartScript.event_chance + "," + smartScript.event_flags + "," + eventParameters[0] + "," +
                                              eventParameters[1] + "," + eventParameters[2] + "," + eventParameters[3] + "," + smartScript.action_type + "," +
                                              actionParameters[0] + "," + actionParameters[1] + "," + actionParameters[2] + "," + actionParameters[3] + "," +
                                              actionParameters[4] + "," + actionParameters[5] + "," + smartScript.target_type + "," + targetParameters[0] + "," +
                                              targetParameters[1] + "," + targetParameters[2] + "," + smartScript.target_x + "," + smartScript.target_y + "," +
                                              smartScript.target_z + "," + smartScript.target_o + "," + '"' + smartScript.comment + '"' + ")";

                if (i == smartScripts.Count - 1)
                    generatedSql += ";";
                else
                    generatedSql += ",";

                generatedSql += "\n"; //! White line at end of script to make it easier to select
            }

            //! Replaces '<entry>0[id]' ([id] being like 00, 01, 02, 03, etc.) by '@ENTRY*100+03' for example.
            //! Example: replaces 2891401 by @ENTRY*100+01 if original entryorguid is 28914.
            for (int i = 0; i < 50; ++i) // Regex.Matches(generatedSql, originalEntryOrGuidAndSourceType.entryOrGuid + "0" + i.ToString()).Count
            {
                string[] charactersToReplace = new string[3] { ",", ")", " " };

                for (int j = 0; j < 3; ++j)
                {
                    string characterToReplace = charactersToReplace[j];
                    string stringToReplace = originalEntryOrGuidAndSourceType.entryOrGuid + "0" + i.ToString() + characterToReplace;

                    if (!generatedSql.Contains(stringToReplace))
                    {
                        stringToReplace = originalEntryOrGuidAndSourceType.entryOrGuid + "0" + i.ToString() + ")";

                        if (!generatedSql.Contains(stringToReplace))
                            continue;
                    }

                    string _i = i.ToString();

                    if (i < 10)
                        _i = "0" + _i.Substring(0);

                    generatedSql = generatedSql.Replace(stringToReplace, sourceSet + "*100+" + _i + characterToReplace);
                }
            }

            return generatedSql;
        }

        private async Task<string> GenerateSmartAiRevertQuery()
        {
            if (!Settings.Default.UseWorldDatabase)
                return String.Empty;

            string revertQuery = String.Empty;
            List<EntryOrGuidAndSourceType> entriesOrGuidsAndSourceTypes = SAI_Editor_Manager.Instance.GetUniqueEntriesOrGuidsAndSourceTypes(listViewSmartScripts.SmartScripts);

            foreach (EntryOrGuidAndSourceType entryOrGuidAndSourceType in entriesOrGuidsAndSourceTypes)
            {
                List<SmartScript> smartScripts = await SAI_Editor_Manager.Instance.worldDatabase.GetSmartScripts(entryOrGuidAndSourceType.entryOrGuid, (int)entryOrGuidAndSourceType.sourceType);
                string scriptName = String.Empty, aiName = String.Empty;

                switch (entryOrGuidAndSourceType.sourceType)
                {
                    case SourceTypes.SourceTypeCreature:
                        scriptName = await SAI_Editor_Manager.Instance.worldDatabase.GetCreatureScriptNameById(entryOrGuidAndSourceType.entryOrGuid);
                        aiName = await SAI_Editor_Manager.Instance.worldDatabase.GetCreatureAiNameById(entryOrGuidAndSourceType.entryOrGuid);

                        revertQuery += "UPDATE creature_template SET Ainame='" + aiName + "',Scriptname='" + scriptName + "' WHERE entry='" + entryOrGuidAndSourceType.entryOrGuid + "';";
                        break;
                    case SourceTypes.SourceTypeGameobject:
                        scriptName = await SAI_Editor_Manager.Instance.worldDatabase.GetGameobjectScriptNameById(entryOrGuidAndSourceType.entryOrGuid);
                        aiName = await SAI_Editor_Manager.Instance.worldDatabase.GetGameobjectAiNameById(entryOrGuidAndSourceType.entryOrGuid);

                        revertQuery += "UPDATE gameobject_template SET Ainame='" + aiName + "',Scriptname='" + await SAI_Editor_Manager.Instance.worldDatabase.GetGameobjectScriptNameById(entryOrGuidAndSourceType.entryOrGuid) + "' WHERE entry='" + entryOrGuidAndSourceType.entryOrGuid + "';";
                        break;
                    case SourceTypes.SourceTypeAreaTrigger:
                        scriptName = await SAI_Editor_Manager.Instance.worldDatabase.GetAreaTriggerScriptNameById(entryOrGuidAndSourceType.entryOrGuid);

                        if (scriptName != String.Empty)
                            revertQuery += "UPDATE areatrigger_scripts SET Scriptname='" + scriptName + "' WHERE entry='" + entryOrGuidAndSourceType.entryOrGuid + "';";
                        else
                            revertQuery += "DELETE FROM areatrigger_scripts WHERE entry='" + entryOrGuidAndSourceType.entryOrGuid + "';";

                        break;
                }

                if (smartScripts != null && smartScripts.Count > 0)
                {
                    revertQuery += "DELETE FROM smart_scripts WHERE entryorguid=" + smartScripts[0].entryorguid.ToString() + ";";
                    revertQuery += "REPLACE INTO smart_scripts VALUES ";

                    for (int i = 0; i < smartScripts.Count; ++i)
                    {
                        SmartScript smartScript = smartScripts[i];
                        revertQuery += "(";
                        revertQuery += String.Format("{0},{1},{2},{3},", smartScript.entryorguid, smartScript.source_type, smartScript.id, smartScript.link);
                        revertQuery += String.Format("{0},{1},{2},{3},", smartScript.event_type, smartScript.event_phase_mask, smartScript.event_chance, smartScript.event_flags);
                        revertQuery += String.Format("{0},{1},{2},{3},", smartScript.event_param1, smartScript.event_param2, smartScript.event_param3, smartScript.event_param4);
                        revertQuery += String.Format("{0},{1},{2},{3},", smartScript.action_type, smartScript.action_param1, smartScript.action_param2, smartScript.action_param3);
                        revertQuery += String.Format("{0},{1},{2},{3},", smartScript.action_param4, smartScript.action_param5, smartScript.action_param6, smartScript.target_type);
                        revertQuery += String.Format("{0},{1},{2},{3},", smartScript.target_param1, smartScript.target_param2, smartScript.target_param3, smartScript.target_x);
                        revertQuery += String.Format("{0},{1},{2}," + '"' + "{3}" + '"', smartScript.target_y, smartScript.target_z, smartScript.target_o, smartScript.comment);
                        revertQuery += ")";

                        if (i == smartScripts.Count - 1)
                            revertQuery += ";";
                        else
                            revertQuery += ",";
                    }
                }
                else
                    revertQuery += "DELETE FROM smart_scripts WHERE entryorguid='" + entryOrGuidAndSourceType.entryOrGuid + "';";
            }

            return revertQuery;
        }

        public async void GenerateCommentsForAllItems()
        {
            if (listViewSmartScripts.SmartScripts.Count == 0)
                return;

            for (int i = 0; i < listViewSmartScripts.SmartScripts.Count; ++i)
            {
                SmartScript smartScript = listViewSmartScripts.SmartScripts[i];
                SmartScript smartScriptLink = GetInitialSmartScriptLink(smartScript);
                string newComment = await CommentGenerator.Instance.GenerateCommentFor(smartScript, originalEntryOrGuidAndSourceType, true, smartScriptLink);
                smartScript.comment = newComment;
                listViewSmartScripts.ReplaceSmartScript(smartScript);
                FillFieldsBasedOnSelectedScript();
            }

            textBoxComments.Text = listViewSmartScripts.SelectedSmartScript.comment;
        }

        private void buttonGenerateComments_Click(object sender, EventArgs e)
        {
            if (!Settings.Default.UseWorldDatabase)
                return;

            GenerateCommentsForAllItems();
            ResizeColumns();
            listViewSmartScripts.Select();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveLastUsedFields();
        }

        private void SaveLastUsedFields()
        {
            Settings.Default.LastEntryOrGuid = textBoxEntryOrGuid.Text;
            Settings.Default.LastSourceType = comboBoxSourceType.SelectedIndex;
            Settings.Default.ShowBasicInfo = checkBoxShowBasicInfo.Checked;
            Settings.Default.LockSmartScriptId = checkBoxLockEventId.Checked;
            Settings.Default.ListActionLists = checkBoxListActionlistsOrEntries.Checked;
            Settings.Default.AllowChangingEntryAndSourceType = checkBoxAllowChangingEntryAndSourceType.Checked;
            Settings.Default.PhaseHighlighting = checkBoxUsePhaseColors.Checked;
            Settings.Default.ShowTooltipsPermanently = checkBoxUsePermanentTooltips.Checked;

            if (formState == FormState.FormStateMain)
                Settings.Default.MainFormHeight = Height;

            if (formState == FormState.FormStateLogin)
            {
                RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
                byte[] buffer = new byte[1024];
                rng.GetBytes(buffer);
                string salt = BitConverter.ToString(buffer);
                rng.Dispose();

                Settings.Default.Entropy = salt;
                Settings.Default.Host = textBoxHost.Text;
                Settings.Default.User = textBoxUsername.Text;
                Settings.Default.Password = textBoxPassword.Text.Length == 0 ? String.Empty : textBoxPassword.Text.ToSecureString().EncryptString(Encoding.Unicode.GetBytes(salt));
                Settings.Default.Database = textBoxWorldDatabase.Text;
                Settings.Default.Port = XConverter.ToUInt32(textBoxPort.Text);
                Settings.Default.UseWorldDatabase = radioButtonConnectToMySql.Checked;
                Settings.Default.AutoConnect = checkBoxAutoConnect.Checked;
            }

            Settings.Default.Save();
        }

        private void ResizeColumns()
        {
            foreach (ColumnHeader header in listViewSmartScripts.Columns)
                header.AutoResize(ColumnHeaderAutoResizeStyle.HeaderSize);
        }

        private async Task GenerateCommentForSmartScript(SmartScript smartScript, bool resize = true)
        {
            if (smartScript == null || !Settings.Default.GenerateComments)
                return;

            SmartScript smartScriptLink = GetInitialSmartScriptLink(smartScript);
            string newComment = smartScript.comment;

            if (!updatingFieldsBasedOnSelectedScript)
            {
                newComment = await CommentGenerator.Instance.GenerateCommentFor(smartScript, originalEntryOrGuidAndSourceType, true, smartScriptLink);

                if (smartScript.link != 0 && (SmartEvent)smartScript.event_type != SmartEvent.SMART_EVENT_LINK)
                    await GenerateCommentForAllEventsLinkingFromSmartScript(smartScript);
            }

            //! For some reason we have to re-check it here...
            if (listViewSmartScripts.SelectedItems.Count == 0)
                return;

            string oldComment = smartScript.comment;
            smartScript.comment = newComment;
            listViewSmartScripts.ReplaceSmartScript(smartScript);
            FillFieldsBasedOnSelectedScript();

            if (oldComment != newComment)
                ResizeColumns();
        }

        private async Task GenerateCommentForAllEventsLinkingFromSmartScript(SmartScript smartScript)
        {
            if (smartScript == null || !Settings.Default.GenerateComments || smartScript.link == 0)
                return;

            List<SmartScript> smartScriptsLinkedFrom = GetAllSmartScriptThatLinkFrom(smartScript);

            if (smartScriptsLinkedFrom == null || smartScriptsLinkedFrom.Count == 0)
                return;

            for (int i = 0; i < smartScriptsLinkedFrom.Count; ++i)
            {
                SmartScript smartScriptListView = smartScriptsLinkedFrom[i];

                if (smartScriptListView.entryorguid != smartScript.entryorguid)
                    continue;

                smartScriptListView.comment = await CommentGenerator.Instance.GenerateCommentFor(smartScriptListView, originalEntryOrGuidAndSourceType, true, smartScript);
                listViewSmartScripts.ReplaceSmartScript(smartScriptListView);
            }
        }

        private async Task GenerateCommentForAllEventsLinkingFromAndToSmartScript(SmartScript smartScript)
        {
            if (smartScript == null || !Settings.Default.GenerateComments)
                return;

            for (int i = 0; i < listViewSmartScripts.SmartScripts.Count; ++i)
            {
                SmartScript smartScriptListView = listViewSmartScripts.SmartScripts[i];

                if (smartScriptListView.entryorguid != smartScript.entryorguid)
                    continue;

                if (smartScript.link == smartScriptListView.id)
                {
                    smartScriptListView.comment = await CommentGenerator.Instance.GenerateCommentFor(smartScriptListView, originalEntryOrGuidAndSourceType, true, GetInitialSmartScriptLink(smartScriptListView));
                    listViewSmartScripts.ReplaceSmartScript(smartScriptListView);
                }
                else if (smartScriptListView.link == smartScript.id)
                {
                    smartScript.comment = await CommentGenerator.Instance.GenerateCommentFor(smartScript, originalEntryOrGuidAndSourceType, true, GetInitialSmartScriptLink(smartScript));
                    listViewSmartScripts.ReplaceSmartScript(smartScript);
                }
            }
        }

        private SmartScript GetInitialSmartScriptLink(SmartScript smartScript)
        {
            if (smartScript == null || (SmartEvent)smartScript.event_type != SmartEvent.SMART_EVENT_LINK)
                return null;

            SmartScript smartScriptLink = null;
            int idToCheck = smartScript.id;

        GetLinkForCurrentSmartScriptLink:
            foreach (SmartScript smartScriptInListView in listViewSmartScripts.SmartScripts)
            {
                if (smartScriptInListView.link == idToCheck)
                {
                    smartScriptLink = smartScriptInListView;
                    break;
                }
            }

            if (smartScriptLink != null && (SmartEvent)smartScriptLink.event_type == SmartEvent.SMART_EVENT_LINK)
            {
                idToCheck = smartScriptLink.id;
                goto GetLinkForCurrentSmartScriptLink;
            }

            return smartScriptLink;
        }

        //! MUST take initial smartscript of linkings
        private List<SmartScript> GetAllSmartScriptThatLinkFrom(SmartScript smartScriptInitial)
        {
            if (smartScriptInitial == null || smartScriptInitial.link == 0)
                return null;

            List<SmartScript> smartScriptsLinking = new List<SmartScript>();
            smartScriptsLinking.Add(smartScriptInitial);
            SmartScript lastInitialSmartScript = smartScriptInitial;

            foreach (SmartScript smartScriptInListView in listViewSmartScripts.SmartScripts)
            {
                if ((SmartEvent)smartScriptInListView.event_type != SmartEvent.SMART_EVENT_LINK)
                    continue;

                if (smartScriptInListView.id == lastInitialSmartScript.link)
                {
                    smartScriptsLinking.Add(smartScriptInListView);
                    lastInitialSmartScript = smartScriptInListView;
                }
            }

            return smartScriptsLinking;
        }

        private void menuItemRevertQuery_Click(object sender, EventArgs e)
        {
            if (formState != FormState.FormStateMain)
                return;

            using (RevertQueryForm revertQueryForm = new RevertQueryForm())
                revertQueryForm.ShowDialog(this);
        }

        private void checkBoxShowBasicInfo_CheckedChanged(object sender, EventArgs e)
        {
            HandleShowBasicInfo();
        }

        private void HandleShowBasicInfo()
        {
            int prevSelectedIndex = listViewSmartScripts.SelectedItems.Count > 0 ? listViewSmartScripts.SelectedItems[0].Index : 0;

            List<string> properties = new List<string>();

            properties.Add("event_phase_mask");
            properties.Add("event_chance");
            properties.Add("event_flags");
            properties.Add("event_param1");
            properties.Add("event_param2");
            properties.Add("event_param3");
            properties.Add("event_param4");
            properties.Add("action_param1");
            properties.Add("action_param2");
            properties.Add("action_param3");
            properties.Add("action_param4");
            properties.Add("action_param5");
            properties.Add("action_param6");
            properties.Add("target_param1");
            properties.Add("target_param2");
            properties.Add("target_param3");
            properties.Add("target_x");
            properties.Add("target_y");
            properties.Add("target_z");
            properties.Add("target_o");

            if (checkBoxShowBasicInfo.Checked)
                listViewSmartScripts.ExcludeProperties(properties);
            else
                listViewSmartScripts.IncludeProperties(properties);

            if (listViewSmartScripts.Items.Count > prevSelectedIndex)
            {
                listViewSmartScripts.Items[prevSelectedIndex].Selected = true;
                listViewSmartScripts.EnsureVisible(prevSelectedIndex);
            }

            listViewSmartScripts.Select(); //! Sets the focus on the listview
        }

        private async void menuItemGenerateCommentListView_Click(object sender, EventArgs e)
        {
            if (formState != FormState.FormStateMain || listViewSmartScripts.SelectedSmartScript == null || !Settings.Default.UseWorldDatabase)
                return;

            for (int i = 0; i < listViewSmartScripts.SmartScripts.Count; ++i)
            {
                SmartScript smartScript = listViewSmartScripts.SmartScripts[i];

                if (smartScript != listViewSmartScripts.SelectedSmartScript)
                    continue;

                SmartScript smartScriptLink = GetInitialSmartScriptLink(smartScript);
                string newComment = await CommentGenerator.Instance.GenerateCommentFor(smartScript, originalEntryOrGuidAndSourceType, true, smartScriptLink);
                smartScript.comment = newComment;
                listViewSmartScripts.ReplaceSmartScript(smartScript);
                FillFieldsBasedOnSelectedScript();
            }

            textBoxComments.Text = listViewSmartScripts.SelectedSmartScript.comment;
        }

        private void menuItemDuplicateSelectedRow_Click(object sender, EventArgs e)
        {
            if (formState != FormState.FormStateMain || listViewSmartScripts.SelectedSmartScript == null)
                return;

            listViewSmartScripts.EnsureVisible(listViewSmartScripts.AddSmartScript(listViewSmartScripts.SelectedSmartScript.Clone(), false, true));
        }

        private void textBoxEventType_MouseWheel(object sender, MouseEventArgs e)
        {
            int newNumber = XConverter.ToInt32(textBoxEventType.Text);

            if (e.Delta > 0)
                newNumber--;
            else
                newNumber++;

            if (newNumber < 0)
                newNumber = 0;

            if (newNumber > (int)MaxValues.MaxEventType)
                newNumber = (int)MaxValues.MaxEventType;

            textBoxEventType.Text = newNumber.ToString();
        }

        private void textBoxActionType_MouseWheel(object sender, MouseEventArgs e)
        {
            int newNumber = XConverter.ToInt32(textBoxActionType.Text);

            if (e.Delta > 0)
                newNumber--;
            else
                newNumber++;

            if (newNumber < 0)
                newNumber = 0;

            if (newNumber > (int)MaxValues.MaxActionType)
                newNumber = (int)MaxValues.MaxActionType;

            textBoxActionType.Text = newNumber.ToString();
        }

        private void textBoxTargetType_MouseWheel(object sender, MouseEventArgs e)
        {
            int newNumber = XConverter.ToInt32(textBoxTargetType.Text);

            if (e.Delta > 0)
                newNumber--;
            else
                newNumber++;

            if (newNumber < 0)
                newNumber = 0;

            if (newNumber > (int)MaxValues.MaxTargetType)
                newNumber = (int)MaxValues.MaxTargetType;

            textBoxTargetType.Text = newNumber.ToString();
        }

        private void menuItemLoadSelectedEntry_Click(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedSmartScript == null)
                return;

            int entryorguid = listViewSmartScripts.SelectedSmartScript.entryorguid;
            SourceTypes source_type = (SourceTypes)listViewSmartScripts.SelectedSmartScript.source_type;
            listViewSmartScripts.ReplaceSmartScripts(new List<SmartScript>());
            listViewSmartScripts.Items.Clear();
            TryToLoadScript(entryorguid, source_type);
        }

        private async void textBoxId_TextChanged(object sender, EventArgs e)
        {
            if (listViewSmartScripts.SelectedItems.Count > 0)
            {
                listViewSmartScripts.SelectedSmartScript.id = XConverter.ToInt32(textBoxId.Text);
                listViewSmartScripts.ReplaceSmartScript(listViewSmartScripts.SelectedSmartScript);
                await GenerateCommentForSmartScript(listViewSmartScripts.SelectedSmartScript);
            }
        }

        private void radioButtonConnectToMySql_CheckedChanged(object sender, EventArgs e)
        {
            HandleRadioButtonUseDatabaseChanged();
        }

        private void radioButtonDontUseDatabase_CheckedChanged(object sender, EventArgs e)
        {
            HandleRadioButtonUseDatabaseChanged();
        }

        private void HandleRadioButtonUseDatabaseChanged()
        {
            textBoxHost.Enabled = radioButtonConnectToMySql.Checked;
            textBoxUsername.Enabled = radioButtonConnectToMySql.Checked;
            textBoxPassword.Enabled = radioButtonConnectToMySql.Checked;
            textBoxWorldDatabase.Enabled = radioButtonConnectToMySql.Checked;
            textBoxPort.Enabled = radioButtonConnectToMySql.Checked;
            buttonSearchWorldDb.Enabled = radioButtonConnectToMySql.Checked;
            labelDontUseDatabaseWarning.Visible = !radioButtonConnectToMySql.Checked;

            HandleHeightLoginFormBasedOnuseDatabaseSetting();
        }

        private void HandleHeightLoginFormBasedOnuseDatabaseSetting()
        {
            if (formState != FormState.FormStateMain)
            {
                if (radioButtonConnectToMySql.Checked)
                {
                    MaximumSize = new Size((int)FormSizes.LoginFormWidth, (int)FormSizes.LoginFormHeight);
                    Height = (int)FormSizes.LoginFormHeight;
                }
                else
                {
                    MaximumSize = new Size((int)FormSizes.LoginFormWidth, (int)FormSizes.LoginFormHeightShowWarning);
                    Height = (int)FormSizes.LoginFormHeightShowWarning;
                }
            }
        }

        public void HandleUseWorldDatabaseSettingChanged()
        {
            radioButtonConnectToMySql.Checked = Settings.Default.UseWorldDatabase;
            radioButtonDontUseDatabase.Checked = !Settings.Default.UseWorldDatabase;
            buttonSearchForEntryOrGuid.Enabled = Settings.Default.UseWorldDatabase || comboBoxSourceType.SelectedIndex == 2;
            pictureBoxLoadScript.Enabled = textBoxEntryOrGuid.Text.Length > 0 && Settings.Default.UseWorldDatabase;
            checkBoxListActionlistsOrEntries.Enabled = Settings.Default.UseWorldDatabase;
            menuItemRevertQuery.Enabled = Settings.Default.UseWorldDatabase;
            SetGenerateCommentsEnabled(listViewSmartScripts.Items.Count > 0 && Settings.Default.UseWorldDatabase);

            if (Settings.Default.UseWorldDatabase)
                Text = "SAI-Editor " + applicationVersion + " - Connection: " + Settings.Default.User + ", " + Settings.Default.Host + ", " + Settings.Default.Port.ToString();
            else
                Text = "SAI-Editor " + applicationVersion + " - Creator-only mode, no database connection";
        }

        private void menuItemRetrieveLastDeletedRow_Click(object sender, EventArgs e)
        {
            if (lastDeletedSmartScripts.Count == 0)
            {
                MessageBox.Show("There are no items deleted in this session ready to be restored.", "Nothing to retrieve!", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            listViewSmartScripts.AddSmartScript(lastDeletedSmartScripts.Last());
            lastDeletedSmartScripts.Remove(lastDeletedSmartScripts.Last());
        }

        private void checkBoxUsePhaseColors_CheckedChanged(object sender, EventArgs e)
        {
            Settings.Default.PhaseHighlighting = checkBoxUsePhaseColors.Checked;
            Settings.Default.Save();

            listViewSmartScripts.Init(true);
        }

        private void checkBoxUsePermanentTooltips_CheckedChanged(object sender, EventArgs e)
        {
            checkBoxUsePermanentTooltips.Enabled = false;
            Settings.Default.ShowTooltipsPermanently = checkBoxUsePermanentTooltips.Checked;
            Settings.Default.Save();

            ExpandToShowPermanentTooltips(!checkBoxUsePermanentTooltips.Checked);
        }

        private void checkForUpdatesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show("Opening the updater will close the SAI-Editor. The SAI-Editor will be opened afterwards. Are you sure you wish to continue?", "Do you wish to continue?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.No)
                return;

            Close();

            try
            {
                Process.Start(Directory.GetCurrentDirectory() + "\\SAI-Editor Updater.exe");
            }
            catch (Exception)
            {
                MessageBox.Show("The updater could not be opened.", "Something went wrong!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
