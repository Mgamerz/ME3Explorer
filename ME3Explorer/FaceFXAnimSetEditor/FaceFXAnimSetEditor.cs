﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ME3Explorer.Unreal;
using ME3Explorer.Unreal.Classes;
using Be.Windows.Forms;
using ME3Explorer.Packages;

namespace ME3Explorer.FaceFXAnimSetEditor
{
    public partial class FaceFXAnimSetEditor : Form
    {
        public ME3Package pcc;
        public List<int> Objects;
        public FaceFXAnimSet FaceFX;

        public FaceFXAnimSetEditor()
        {
            InitializeComponent();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = "*.pcc|*.pcc";
            if (d.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    pcc = new ME3Package(d.FileName);
                    Objects = new List<int>();
                    IReadOnlyList<IExportEntry> Exports = pcc.Exports;
                    for (int i = 0; i < Exports.Count; i++)
                        if (Exports[i].ClassName == "FaceFXAnimSet")
                            Objects.Add(i);
                    ListRefresh();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error:\n" + ex.Message);
                }
            }
        }

        public void ListRefresh()
        {
            listBox1.Items.Clear();
            foreach(int n in Objects)
                listBox1.Items.Add("#" + n + " : " + pcc.Exports[n].GetFullPath);
        }

        private void FaceFXRefresh(int n, IEnumerable<string> expandedNodes = null, string topNodeName = null)
        {
            if (FaceFX == null)
                return;
            hb1.ByteProvider = new DynamicByteProvider(pcc.Exports[Objects[n]].Data);
            treeView1.Nodes.Clear();
            treeView1.Nodes.Add(FaceFX.HeaderToTree());
            nameAllNodes(treeView1.Nodes);
            treeView2.Nodes.Clear();
            treeView2.Nodes.Add(FaceFX.DataToTree());
            nameAllNodes(treeView2.Nodes);
            TreeNode[] nodes;
            if (expandedNodes != null)
            {
                foreach (string item in expandedNodes)
                {
                    nodes = treeView2.Nodes.Find(item, true);
                    if (nodes.Length > 0)
                    {
                        foreach (var node in nodes)
                        {
                            node.Expand();
                        }
                    }
                }
            }
            nodes = treeView2.Nodes.Find(topNodeName, true);
            if (nodes.Length > 0)
            {
                treeView2.TopNode = nodes[0];
            }
        }

        private void nameAllNodes(TreeNodeCollection Nodes)
        {
            List<TreeNode> allNodes = Nodes.Cast<TreeNode>().ToList();
            //flatten tree of nodes into list.
            for (int i = 0; i < allNodes.Count(); i++)
            {
                allNodes[i].Name = i.ToString();
                allNodes.AddRange(allNodes[i].Nodes.Cast<TreeNode>());
            }
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            int n = listBox1.SelectedIndex;
            if (n == -1)
                return;
            FaceFX = new FaceFXAnimSet(pcc, Objects[n]);
            FaceFXRefresh(n);
        }

        private void recreateAndDumpToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int n = listBox1.SelectedIndex;
            if (n == -1)
                return;
            SaveFileDialog d = new SaveFileDialog();
            d.FileName = pcc.Exports[Objects[n]].ObjectName + ".fxa";
            d.Filter = "*.fxa|*.fxa";
            if(d.ShowDialog() == DialogResult.OK)
            {
                FaceFX.DumpToFile(d.FileName);
                MessageBox.Show("Done.");
            }
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (pcc == null)
                return;
            pcc.save();
            MessageBox.Show("Done.");
        }

        private void saveChangesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (FaceFX == null)
                return;
            FaceFX.Save();
            MessageBox.Show("Done.");
        }

        private void treeView2_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            TreeNode t = treeView2.SelectedNode;
            if (t == null || t.Parent == null)
                return;
            TreeNode t1 = t.Parent;
            if (t1 == null || t1.Parent == null)
                return;
            TreeNode t2 = t1.Parent;
            if (t2 == null || t2.Parent == null)
                return;
            string result; int i; float f = 0;
            if (t2.Text == "Entries")
            {
                int entidx = t1.Index;
                int subidx = t.Index;
                FaceFXAnimSet.DataAnimSetDataStruct d = FaceFX.Data.Data[entidx];
                switch (subidx)
                {
                    case 0://unk1
                        result = Microsoft.VisualBasic.Interaction.InputBox("Please enter new value", "ME3Explorer", d.unk1.ToString(), 0, 0);
                        i = -1;
                        if (int.TryParse(result, out i) && i >= 0 && i < FaceFX.Header.Names.Length)
                            d.unk1 = i;
                        break;
                    case 4://FadeInTime
                        result = Microsoft.VisualBasic.Interaction.InputBox("Please enter new value", "ME3Explorer", d.FadeInTime.ToString(), 0, 0);
                        if (float.TryParse(result, out f))
                            d.FadeInTime = f;
                        break;
                    case 5://FadeInTime
                        result = Microsoft.VisualBasic.Interaction.InputBox("Please enter new value", "ME3Explorer", d.FadeOutTime.ToString(), 0, 0);
                        if (float.TryParse(result, out f))
                            d.FadeOutTime = f;
                        break;
                    case 6://unk2
                        result = Microsoft.VisualBasic.Interaction.InputBox("Please enter new value", "ME3Explorer", d.unk2.ToString(), 0, 0);
                        i = -1;
                        if (int.TryParse(result, out i) && i >= 0 && i < FaceFX.Header.Names.Length)
                            d.unk2 = i;
                        break;
                    case 7://Path
                        d.Path = Microsoft.VisualBasic.Interaction.InputBox("Please enter new value", "ME3Explorer", d.Path, 0, 0);
                        break;
                    case 8://ID
                        d.ID = Microsoft.VisualBasic.Interaction.InputBox("Please enter new value", "ME3Explorer", d.ID, 0, 0);
                        break;
                    case 9://unk3
                        result = Microsoft.VisualBasic.Interaction.InputBox("Please enter new value", "ME3Explorer", d.unk3.ToString(), 0, 0);
                        i = -1;
                        if (int.TryParse(result, out i) && i >= 0 && i < FaceFX.Header.Names.Length)
                            d.unk3 = i;
                        break;
                    default:
                        return;
                }
                FaceFX.Data.Data[entidx] = d;
            }
            else if(t2.Parent.Text == "Entries")
            {
                int entidx = t2.Index;
                int subidx = t1.Index;
                int subsubidx = t.Index;
                FaceFXAnimSet.DataAnimSetDataStruct d = FaceFX.Data.Data[entidx];
                switch (subidx)
                {
                    case 1:
                        FaceFXAnimSet.UnkStruct1 u = d.unklist1[subsubidx];
                        result = Microsoft.VisualBasic.Interaction.InputBox("Please enter new value", "ME3Explorer", u.unk1 + " ; " + u.unk2, 0, 0);
                        string[] reslist = result.Split(';');
                        if (reslist.Length != 2)
                            return;
                        if (int.TryParse(reslist[0].Trim(), out i))
                            u.unk1 = i;
                        else
                            return;
                        if (int.TryParse(reslist[1].Trim(), out i))
                            u.unk2 = i;
                        else
                            return;
                        d.unklist1[subsubidx] = u;
                        break;
                    case 2:
                        FaceFXAnimSet.UnkStruct2 u2 = d.unklist2[subsubidx];
                        result = Microsoft.VisualBasic.Interaction.InputBox("Please enter new value", "ME3Explorer", u2.unk1 + " ; " + u2.unk2 + " ; " + u2.unk3 + " ; " + u2.unk4, 0, 0);
                        reslist = result.Split(';');
                        if (reslist.Length != 4)
                            return;
                        if (float.TryParse(reslist[0].Trim(), out f))
                            u2.unk1 = f;
                        else
                            return;
                        if (float.TryParse(reslist[1].Trim(), out f))
                            u2.unk2 = f;
                        else
                            return;
                        if (float.TryParse(reslist[2].Trim(), out f))
                            u2.unk3 = f;
                        else
                            return;
                        if (float.TryParse(reslist[3].Trim(), out f))
                            u2.unk4 = f;
                        else
                            return;
                        d.unklist2[subsubidx] = u2;
                        break;
                    case 3:
                        result = Microsoft.VisualBasic.Interaction.InputBox("Please enter new value", "ME3Explorer", d.unklist3[subsubidx].ToString(), 0, 0);
                        if (int.TryParse(result.Trim(), out i))
                            d.unklist3[subsubidx] = i;
                        else
                            return;
                        break;
                }
                FaceFX.Data.Data[entidx] = d;
            }
            int n = listBox1.SelectedIndex;
            if (n == -1)
                return;
            List<TreeNode> allNodes = treeView2.Nodes.Cast<TreeNode>().ToList();
            //flatten tree of nodes into list.
            for (int j = 0; j < allNodes.Count(); j++)
            {
                allNodes.AddRange(allNodes[j].Nodes.Cast<TreeNode>());
            }
            var expandedNodes = allNodes.Where(x => x.IsExpanded).Select(x => x.Name);
            FaceFXRefresh(n, expandedNodes, treeView2.TopNode.Name);
        }

        private void cloneEntryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode t = treeView2.SelectedNode;
            if (t == null || t.Parent == null)
                return;
            TreeNode t1 = t.Parent;
            if (t1 == null || t1.Text != "Entries" || FaceFX == null)
                return;
            FaceFX.CloneEntry(t.Index);
            FaceFX.Save();
            int n = listBox1.SelectedIndex;
            listBox1.SelectedIndex = -1;
            listBox1.SelectedIndex = n;
        }

        private void deleteEntryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode t = treeView2.SelectedNode;
            if (t == null || t.Parent == null)
                return;
            TreeNode t1 = t.Parent;
            if (t1 == null || t1.Text != "Entries" || FaceFX == null)
                return;
            FaceFX.RemoveEntry(t.Index);
            FaceFX.Save();
            int n = listBox1.SelectedIndex;
            listBox1.SelectedIndex = -1;
            listBox1.SelectedIndex = n;
        }

        private void moveEntryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TreeNode t = treeView2.SelectedNode;
            if (t == null || t.Parent == null)
                return;
            TreeNode t1 = t.Parent;
            if (t1 == null || t1.Text != "Entries" || FaceFX == null)
                return;
            string result = Microsoft.VisualBasic.Interaction.InputBox("Please enter new index", "ME3Explorer", t.Index.ToString(), 0, 0);
            int i = 0;
            if (int.TryParse(result, out i))
            {
                FaceFX.MoveEntry(t.Index, i);
                FaceFX.Save();
                int n = listBox1.SelectedIndex;
                listBox1.SelectedIndex = -1;
                listBox1.SelectedIndex = n;
            }
        }

        private void addNameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (FaceFX == null)
                return;
            string result = Microsoft.VisualBasic.Interaction.InputBox("Please enter new name to add", "ME3Explorer", "", 0, 0);
            if (result != "")
            {
                FaceFX.AddName(result);
                FaceFX.Save();
                int n = listBox1.SelectedIndex;
                listBox1.SelectedIndex = -1;
                listBox1.SelectedIndex = n;
            }
        }
    }
}
