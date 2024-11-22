using System.Collections;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using GitCommands;
using GitUI.HelperDialogs;
using GitUI.Properties;
using GitUIPluginInterfaces;
using ResourceManager;
using YamlDotNet.Serialization;

namespace GitUI.CommandsDialogs
{
    public partial class FormArchive : GitModuleForm
    {
        private readonly TranslationString _saveFileDialogFilterZip =
            new("Zip file (*.zip)");

        private readonly TranslationString _saveFileDialogFilterTar =
            new("Tar file (*.tar)");

        private readonly TranslationString _saveFileDialogCaption =
            new("Save archive as");

        private readonly TranslationString _noRevisionSelected =
            new("You need to choose a target revision.");

        private GitRevision? _selectedRevision;
        public GitRevision? SelectedRevision
        {
            get { return _selectedRevision; }
            set
            {
                _selectedRevision = value;
                commitSummaryUserControl1.Revision = _selectedRevision;
            }
        }

        private GitRevision? _diffSelectedRevision;
        private GitRevision? DiffSelectedRevision
        {
            get { return _diffSelectedRevision; }
            set
            {
                _diffSelectedRevision = value;
                ////commitSummaryUserControl2.Revision = _diffSelectedRevision;
                if (_diffSelectedRevision is null)
                {
                    const string defaultString = "...";
                    labelDateCaption.Text = $"{ResourceManager.TranslatedStrings.CommitDate}:";
                    labelAuthor.Text = defaultString;
                    gbDiffRevision.Text = defaultString;
                    labelMessage.Text = defaultString;
                }
                else
                {
                    labelDateCaption.Text = $"{ResourceManager.TranslatedStrings.CommitDate}: {_diffSelectedRevision.CommitDate}";
                    labelAuthor.Text = _diffSelectedRevision.Author;
                    gbDiffRevision.Text = _diffSelectedRevision.ObjectId.ToShortString();
                    labelMessage.Text = _diffSelectedRevision.Subject;
                }
            }
        }

        public void SetDiffSelectedRevision(GitRevision? revision)
        {
            checkboxRevisionFilter.Checked = revision is not null;
            DiffSelectedRevision = revision;
        }

        public void SetPathArgument(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                checkBoxPathFilter.Checked = false;
                textBoxPaths.Text = "";
            }
            else
            {
                checkBoxPathFilter.Checked = true;
                textBoxPaths.Text = path;
            }
        }

        private enum OutputFormat
        {
            Zip,
            Tar
        }

        [Obsolete("For VS designer and translation test only. Do not remove.")]
        private FormArchive()
        {
            InitializeComponent();
        }

        public FormArchive(GitUICommands commands)
            : base(commands)
        {
            InitializeComponent();
            InitializeComplete();

            labelAuthor.Font = new System.Drawing.Font(labelAuthor.Font, System.Drawing.FontStyle.Bold);
            labelMessage.Font = new System.Drawing.Font(labelMessage.Font, System.Drawing.FontStyle.Bold);
        }

        private Dictionary<string, List<string>> _bundlesPreset = new();
        private void FormArchive_Load(object sender, EventArgs e)
        {
            buttonArchiveRevision.Focus();
            checkBoxPathFilter_CheckedChanged(this, EventArgs.Empty);
            checkboxRevisionFilter_CheckedChanged(this, EventArgs.Empty);
            var pathSrc = Path.Combine(Module.WorkingDir, "src");
            if (Directory.Exists(pathSrc))
            {
                List<string> bundles = new();

                // è crm (forse)
                foreach (var path in Directory.GetDirectories(pathSrc))
                {
                    if (path.EndsWith("Bundle"))
                    {
                        var tmp = path;
                        if (tmp.EndsWith("\\"))
                        {
                            tmp = tmp.Substring(0, tmp.Length - 1);
                        }

                        tmp = tmp.Substring(tmp.LastIndexOf("\\") + 1);
                        bundles.Add(tmp);
                    }
                }

                bundles.Sort();
                lstBundles.DataSource = bundles.ToArray();
                lstBundles.SelectedIndex = -1;
                var pathConfig = Path.Combine(Module.WorkingDir, "config");
                _bundlesPreset.Clear();
                _bundlesPreset.Add("Nessuno", new List<string>());
                foreach (var path in Directory.GetFiles(pathConfig))
                {
                    if (path.EndsWith(".yaml") && !path.Contains("parameters.base.yaml"))
                    {
                        var contents = File.ReadAllText(path);
                        if (contents.StartsWith("parameters:"))
                        {
                            List<string> bundlesP = new();
                            var deserializer = new Deserializer();
                            var result = deserializer.Deserialize<Dictionary<string, object>>(new StringReader(contents));
                            foreach (KeyValuePair<object, object> item in (IEnumerable)result["parameters"])
                            {
                                if (item.Key.ToString() == "core_bundles" || item.Key.ToString() == "bundles_azienda" || item.Key.ToString() == "whitelist_bundles")
                                {
                                    foreach (var sub in (IEnumerable)item.Value)
                                    {
                                        bundlesP.Add(sub.ToString().Replace("Bundle", "") + "Bundle");
                                    }
                                }
                            }

                            _bundlesPreset.Add(path.Substring(path.LastIndexOf("\\") + 1).Replace(".yaml", ""), bundlesP);
                        }
                    }
                }

                cmbParameters.DataSource = _bundlesPreset.Keys.ToList();
            }
        }

        private void Save_Click(object sender, EventArgs e)
        {
            if (checkboxRevisionFilter.Checked && DiffSelectedRevision is null)
            {
                MessageBox.Show(this, _noRevisionSelected.Text, TranslatedStrings.Error, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                return;
            }

            string? revision = SelectedRevision?.Guid;

            string fileFilterCaption = GetSelectedOutputFormat() == OutputFormat.Zip ? _saveFileDialogFilterZip.Text : _saveFileDialogFilterTar.Text;
            string fileFilterEnding = GetSelectedOutputFormat() == OutputFormat.Zip ? "zip" : "tar";

            // TODO (feature): if there is a tag on the revision use the tag name as suggestion
            // TODO (feature): let user decide via GUI
            string filenameSuggestion = string.Format("{0}_{1}", new DirectoryInfo(Module.WorkingDir).Name, revision);
            if (checkBoxPathFilter.Checked && textBoxPaths.Lines.Length == 1 && !string.IsNullOrWhiteSpace(textBoxPaths.Lines[0]))
            {
                filenameSuggestion += "_" + textBoxPaths.Lines[0].Trim().Replace(".", "_");
            }

            using SaveFileDialog saveFileDialog = new()
            {
                Filter = string.Format("{0}|*.{1}", fileFilterCaption, fileFilterEnding),
                Title = _saveFileDialogCaption.Text,
                FileName = filenameSuggestion
            };
            if (saveFileDialog.ShowDialog(this) == DialogResult.OK)
            {
                var commitHashFrom = commitSummaryUserControl1.Revision.Guid;
                var commitHashTo = gbDiffRevision.Text;

                List<string> deletedFiles = null;

                if (commitHashTo != "...")
                {
                    try
                    {
                        var arguments1 = string.Format(@"ls-tree -r --name-only {0}", commitHashFrom);
                        var psi = new ProcessStartInfo("git", arguments1);
                        psi.WorkingDirectory = Module.WorkingDir;
                        psi.RedirectStandardError = true;
                        psi.RedirectStandardOutput = true;
                        var p = Process.Start(psi);

                        var output = new List<byte>();
                        var chr = p.StandardOutput.Read();
                        while (chr >= 0)
                        {
                            output.Add((byte)chr);
                            chr = p.StandardOutput.Read();
                        }

                        var s = new MemoryStream(output.ToArray());
                        var sr = new StreamReader(s);

                        var currentContents = (sr.ReadToEnd() ?? "").Split("\n", StringSplitOptions.RemoveEmptyEntries);

                        arguments1 = string.Format(@"diff-tree -r --exit-code --name-only {0} {1}", commitHashFrom, commitHashTo);
                        psi = new ProcessStartInfo("git", arguments1);
                        psi.WorkingDirectory = Module.WorkingDir;
                        psi.RedirectStandardError = true;
                        psi.RedirectStandardOutput = true;
                        p = Process.Start(psi);

                        output = new List<byte>();
                        chr = p.StandardOutput.Read();
                        while (chr >= 0)
                        {
                            output.Add((byte)chr);
                            chr = p.StandardOutput.Read();
                        }

                        s = new MemoryStream(output.ToArray());
                        sr = new StreamReader(s);

                        var diffContents = (sr.ReadToEnd() ?? "").Split("\n", StringSplitOptions.RemoveEmptyEntries);

                        deletedFiles = new List<string>();
                        foreach (var fileNonPresente in diffContents)
                        {
                            if (!currentContents.Contains(fileNonPresente))
                            {
                                deletedFiles.Add(fileNonPresente);
                            }
                        }

                        if (deletedFiles.Count <= 0)
                        {
                            deletedFiles = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Errore generazione .deleted-files:\n" + ex.Message, "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        deletedFiles = null;
                    }
                }

                string format = GetSelectedOutputFormat() == OutputFormat.Zip ? "zip" : "tar";

                var pathTmpWorkaround = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "bmGitExtensions");
                if (!Directory.Exists(pathTmpWorkaround))
                {
                    Directory.CreateDirectory(pathTmpWorkaround);
                }

                pathTmpWorkaround = Path.Combine(pathTmpWorkaround, revision + DateTime.Now.Ticks.ToString());
                Directory.CreateDirectory(pathTmpWorkaround);
                File.WriteAllText(Path.Combine(pathTmpWorkaround, ".commit-hash"), revision);
                if (deletedFiles != null)
                {
                    File.WriteAllText(Path.Combine(pathTmpWorkaround, ".deleted-files"), deletedFiles.Join("\n"));
                }

                string excludes = "";
                if (lstBundles.SelectedItems.Count > 0)
                {
                    foreach (var i in lstBundles.Items)
                    {
                        if (lstBundles.SelectedItems.Contains(i))
                        {
                            continue;
                        }

                        excludes += "\":(exclude)src/" + i + "\" ";
                    }
                }

                var arguments = string.Format(@"archive --format=""{0}"" {1} --output ""{2}"" {3} " + excludes, format, revision, saveFileDialog.FileName, GetPathArgumentFromGui());
                var zipPath = saveFileDialog.FileName;
                FormProcess.ShowDialog(this, arguments, Module.WorkingDir, input: null, useDialogSettings: true);
                try
                {
                    using (var fs = new FileStream(zipPath, FileMode.Open))
                    {
                        using (var z = new ZipArchive(fs, ZipArchiveMode.Update))
                        {
                            z.CreateEntryFromFile(Path.Combine(pathTmpWorkaround, ".commit-hash"), ".commit-hash");
                            if (File.Exists(Path.Combine(pathTmpWorkaround, ".deleted-files")))
                            {
                                z.CreateEntryFromFile(Path.Combine(pathTmpWorkaround, ".deleted-files"), ".deleted-files");
                            }
                        }
                    }
                }
                catch
                {
                    MessageBox.Show("Errore aggiunta file .commit-hash e .deleted-files allo zip", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                if (File.Exists(Path.Combine(pathTmpWorkaround, ".deleted-files")))
                {
                    File.Delete(Path.Combine(pathTmpWorkaround, ".deleted-files"));
                }

                if (File.Exists(Path.Combine(pathTmpWorkaround, ".commit-hash")))
                {
                    File.Delete(Path.Combine(pathTmpWorkaround, ".commit-hash"));
                }

                Directory.Delete(pathTmpWorkaround);

                if (txtTagDeployed.Text.Trim() != "")
                {
                    var tag = txtTagDeployed.Text.Trim();
                    if (!tag.StartsWith("deployed_"))
                    {
                        tag = "deployed_" + tag;
                    }

                    arguments = string.Format(@"push ""origin"" :refs/tags/{0}", tag);
                    FormProcess.ShowDialog(this, arguments, Module.WorkingDir, input: null, useDialogSettings: true);

                    arguments = string.Format(@"tag -f ""{0}""  -- {1}", tag, revision);
                    FormProcess.ShowDialog(this, arguments, Module.WorkingDir, input: null, useDialogSettings: true);

                    arguments = string.Format(@"push --progress ""origin"" tag ""{0}""", tag);
                    FormProcess.ShowDialog(this, arguments, Module.WorkingDir, input: null, useDialogSettings: true);
                }

                Close();
            }
        }

        private string GetPathArgumentFromGui()
        {
            if (checkBoxPathFilter.Checked)
            {
                // 1. get all lines (paths) from text box
                // 2. wrap lines that are not empty with ""
                // 3. join together with space as separator
                return string.Join(" ", textBoxPaths.Lines.Select(a => a.QuoteNE()));
            }
            else if (checkboxRevisionFilter.Checked)
            {
                // 1. get all changed (and not deleted files) from selected to current revision
                var files = UICommands.Module.GetDiffFilesWithUntracked(DiffSelectedRevision?.Guid, SelectedRevision?.Guid, StagedStatus.None).Where(f => !f.IsDeleted);

                // 2. wrap file names with ""
                // 3. join together with space as separator
                return string.Join(" ", files.Select(f => f.Name.QuoteNE()));
            }
            else
            {
                return "";
            }
        }

        private OutputFormat GetSelectedOutputFormat()
        {
            return _NO_TRANSLATE_radioButtonFormatZip.Checked ? OutputFormat.Zip : OutputFormat.Tar;
        }

        private void btnChooseRevision_Click(object sender, EventArgs e)
        {
            using FormChooseCommit chooseForm = new(UICommands, SelectedRevision?.Guid);
            if (chooseForm.ShowDialog(this) == DialogResult.OK && chooseForm.SelectedRevision is not null)
            {
                SelectedRevision = chooseForm.SelectedRevision;
            }
        }

        private void checkBoxPathFilter_CheckedChanged(object sender, EventArgs e)
        {
            textBoxPaths.Enabled = checkBoxPathFilter.Checked;
            if (checkBoxPathFilter.Checked)
            {
                checkboxRevisionFilter.Checked = false;
            }
        }

        private void btnDiffChooseRevision_Click(object sender, EventArgs e)
        {
            using FormChooseCommit chooseForm = new(UICommands, DiffSelectedRevision is not null ? DiffSelectedRevision.Guid : string.Empty);
            if (chooseForm.ShowDialog(this) == DialogResult.OK && chooseForm.SelectedRevision is not null)
            {
                DiffSelectedRevision = chooseForm.SelectedRevision;
            }
        }

        private void checkboxRevisionFilter_CheckedChanged(object sender, EventArgs e)
        {
            btnDiffChooseRevision.Enabled = checkboxRevisionFilter.Checked;
            ////commitSummaryUserControl2.Enabled = checkboxRevisionFilter.Checked;
            ////lblChooseDiffRevision.Enabled = checkboxRevisionFilter.Checked;
            gbDiffRevision.Enabled = checkboxRevisionFilter.Checked;
            btnDiffChooseRevision.Enabled = checkboxRevisionFilter.Checked;
            if (checkboxRevisionFilter.Checked)
            {
                checkBoxPathFilter.Checked = false;
            }
        }

        private void cmbParameters_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmbParameters.SelectedIndex == -1)
            {
                return;
            }

            if (_bundlesPreset.ContainsKey(cmbParameters.SelectedValue.ToString()))
            {
                try
                {
                    lstBundles.SelectedIndices.Clear();
                    foreach (var bundle in _bundlesPreset[cmbParameters.SelectedValue.ToString()])
                    {
                        var list = ((string[])lstBundles.DataSource).ToList();
                        lstBundles.SelectedIndices.Add(list.IndexOf(bundle));
                    }
                }
                catch
                {
                    lstBundles.SelectedIndices.Clear();
                }
            }
        }
    }
}
