﻿using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ME3Explorer
{
    public partial class CondEditor : Form
    {
        public struct Token
        {
            public int type;
            public string Value;
        }

        public Conditionals cond;
        public Conditionals.Entries currentry;
        public int currnr;
        public List<Token> tokenlist = new List<Token>();
        public TreeNode AST = new TreeNode();
        public int TokenPos;
        public byte[] Code = new byte[0];

        public CondEditor()
        {
            InitializeComponent();
        }

        public void setRtb1(string s)
        {
            rtb1.Text = s;
        }

        public bool isWhiteSpace(char c)
        {
            if (c > '\0' && c <= ' ')
                return true;
            else
                return false;
        }

        public bool isQuote(char c)
        {
            if (c == '\"')
                return true;
            else
                return false;
        }

        public bool isLetter(char c)
        {
            return char.IsLetter(c);
        }

        public bool isDigit(char c)
        {
            return char.IsDigit(c);
        }

        public void ReadString()
        {
            int len = rtb1.Text.Length;
            char c;
            Token temp = new Token();
            temp.type = 3;
            temp.Value = "";
            for (int i = 1; TokenPos + i < len; i++)
            {
                c = rtb1.Text[TokenPos + i];
                if (isLetter(c))
                {
                    temp.Value += c;
                    continue;
                }
                if (c == '"')
                {
                    TokenPos += i + 1;
                    tokenlist.Add(temp);
                    return;
                }
            }
            TokenPos += temp.Value.Length;
            tokenlist.Add(temp);
            return;
        }

        public void ReadWord()
        {
            int len = rtb1.Text.Length;
            Token temp = new Token();
            temp.type = 1;
            temp.Value = "";
            char c;
            for (int i = 0; TokenPos + i < len; i++)
            {
                c = rtb1.Text[TokenPos + i];
                if (isLetter(c) || isDigit(c) || c== '-')
                {
                    temp.Value += c;
                }
                else
                {
                    TokenPos += i;
                    tokenlist.Add(temp);
                    return;
                }
            }
            TokenPos += temp.Value.Length;
            tokenlist.Add(temp);
            return;
        }

        public void ReadValue()
        {
            Token temp = new Token();
            int len = rtb1.Text.Length;
            temp.type = 2;
            temp.Value = "";
            char c;
            for (int i = 0; TokenPos + i < len; i++)
            {
                c = rtb1.Text[TokenPos + i];
                if (isDigit(c))
                {
                    temp.Value += c;
                }
                else
                {
                    TokenPos += i;
                    tokenlist.Add(temp);
                    return;
                }
            }
            TokenPos += temp.Value.Length;
            tokenlist.Add(temp);
            return;
        }

        public void ReadSymbol()
        {
            char c = rtb1.Text[TokenPos];
            char c2 = ' ';
            if (TokenPos < rtb1.Text.Length - 1)
                c2 = rtb1.Text[TokenPos + 1];
            Token temp = new Token();
            temp.type = 4;
            temp.Value = "";
            switch (c)
            {
                case '+':
                case '-':
                case '*':
                case '/':
                case '(':
                case ')':
                case '[':
                case ']':
                case ',':
                case '.':
                case ':':
                    temp.Value += c;
                    tokenlist.Add(temp);
                    TokenPos++;
                    return;
                case '=':
                    temp.Value += c;
                    TokenPos++;
                    if (c2 == '=')
                    {
                        TokenPos++;
                        temp.Value += c2;
                        tokenlist.Add(temp);
                        return;
                    }
                    tokenlist.Add(temp);
                    return;
                case '!':
                    temp.Value += c;
                    TokenPos++;
                    if (c2 == '=')
                    {
                        TokenPos++;
                        temp.Value += c2;
                        tokenlist.Add(temp);
                        return;
                    }
                    tokenlist.Add(temp);
                    return;
                case '&':
                    temp.Value += c;
                    TokenPos++;
                    if (c2 == '&')
                    {
                        TokenPos++;
                        temp.Value += c2;
                        tokenlist.Add(temp);
                        return;
                    }
                    tokenlist.Add(temp);
                    return;
                case '|':
                    temp.Value += c;
                    TokenPos++;
                    if (c2 == '|')
                    {
                        TokenPos++;
                        temp.Value += c2;
                        tokenlist.Add(temp);
                        return;
                    }
                    tokenlist.Add(temp);
                    return;
                case '<':
                    temp.Value += c;
                    TokenPos++;
                    if (c2 == '>' || c2 == '=')
                    {
                        TokenPos++;
                        temp.Value += c2;
                        tokenlist.Add(temp);
                        return;
                    }
                    tokenlist.Add(temp);
                    return;
                case '>':
                    temp.Value += c;
                    TokenPos++;
                    if (c2 == '=')
                    {
                        TokenPos++;
                        temp.Value += c2;
                        tokenlist.Add(temp);
                        return;
                    }
                    tokenlist.Add(temp);
                    return;
                default:
                    temp.type = 0;
                    temp.Value = "";
                    tokenlist.Add(temp);
                    TokenPos++;
                    return;

            }
        }

        public bool Tokenizer()
        {
            TokenPos = 0;
            tokenlist = new List<Token>();
            char c;
            while (TokenPos < rtb1.Text.Length)
            {
                c = rtb1.Text[TokenPos];
                if (isWhiteSpace(c))
                {
                    TokenPos++;
                    continue;
                }
                if (isLetter(c))
                {
                    ReadWord();
                    continue;
                }
                if (isDigit(c))
                {
                    ReadValue();
                    continue;
                }
                if (isQuote(c))
                {
                    ReadString();
                    continue;
                }
                ReadSymbol();
            }
            string s = "";
            for (int i = 0; i < tokenlist.Count; i++)
            {
                s += i + " Type:" + tokenlist[i].type + " Value:" + tokenlist[i].Value + "\n";
                if (i >= 7 && (tokenlist[i].Value == "true" || tokenlist[i].Value == "false"))
                {
                    if (tokenlist[i - 1].Value != "bool")
                    {
                        rtb2.Text = s + "\nERROR!";
                        string s2 = "";
                        for (int j = i - 7; j <= i; j++)
                            s2 += tokenlist[j].Value + " ";
                        MessageBox.Show("Missing 'bool' before 'true'/'false' @:\n" + s2);
                        return false;
                    }
                }
            }
            rtb2.Text = s;
            return true;
        }

        public int ReadPlot(int pos, TreeNode node)
        {
            if (pos > tokenlist.Count - 6) return 0;
            Token temp1 = tokenlist[pos];
            Token temp2 = tokenlist[pos + 1];
            Token temp3 = tokenlist[pos + 2];
            Token temp4 = tokenlist[pos + 3];
            Token temp5 = tokenlist[pos + 4];
            Token temp6 = tokenlist[pos + 5];
            if (temp1.Value.ToLower() == "plot" &&
                temp2.Value == "." &&
                temp3.Value.ToLower() == "bools" &&
                temp4.Value == "[" &&
                temp5.type == 2 &&
                temp6.Value == "]")
            {
                TreeNode t1 = new TreeNode("plot bool");
                TreeNode t2 = new TreeNode(temp5.Value);
                t1.Nodes.Add(t2);
                node.Nodes.Add(t1);
            }
            if (temp1.Value.ToLower() == "plot" &&
                temp2.Value == "." &&
                temp3.Value.ToLower() == "ints" &&
                temp4.Value == "[" &&
                temp5.type == 2 &&
                temp6.Value == "]")
            {
                TreeNode t1 = new TreeNode("plot int");
                TreeNode t2 = new TreeNode(temp5.Value);
                t1.Nodes.Add(t2);
                node.Nodes.Add(t1);
            }
            if (temp1.Value.ToLower() == "plot" &&
                temp2.Value == "." &&
                temp3.Value.ToLower() == "floats" &&
                temp4.Value == "[" &&
                temp5.type == 2 &&
                temp6.Value == "]")
            {
                TreeNode t1 = new TreeNode("plot float");
                TreeNode t2 = new TreeNode(temp5.Value);
                t1.Nodes.Add(t2);
                node.Nodes.Add(t1);
            }
            return 6;
        }

        public int ReadBool(int pos, TreeNode node)
        {
            int len = tokenlist.Count;
            if (pos >= len - 1) return 0;
            Token temp = tokenlist[pos];
            Token temp2 = tokenlist[pos + 1];
            if (temp.Value.ToLower() == "bool")
            {
                if (temp2.type == 1)
                {
                    TreeNode t1 = new TreeNode("bool");
                    TreeNode t2 = new TreeNode(temp2.Value.ToLower());
                    t1.Nodes.Add(t2);
                    node.Nodes.Add(t1);
                    return 2;
                }
                return 1;
            }
            return 0;
        }

        public int ReadFunc(int pos, TreeNode node)
        {
            int len = tokenlist.Count;
            if (pos >= len - 5) return 0;
            Token temp = tokenlist[pos + 2];
            Token temp2 = tokenlist[pos + 5];
            TreeNode t1 = new TreeNode("Function");
            TreeNode t2 = new TreeNode(temp.Value);
            TreeNode t3 = new TreeNode(temp2.Value);
            t1.Nodes.Add(t2);
            t1.Nodes.Add(t3);
            node.Nodes.Add(t1);
            return 6;
        }

        public int ReadExpression(int pos, TreeNode node)
        {
            if (pos >= tokenlist.Count - 1) return 0;
            int currpos = pos + 1;
            Token temp2 = tokenlist[currpos];
            Token tahead = new Token();
            if(pos < tokenlist.Count - 1)
                tahead = tokenlist[currpos + 1];
            while (temp2.Value != ")")
            {
                switch (temp2.type)
                {
                    case 0: return 0;
                    case 1:
                        if (temp2.Value.ToLower() == "plot")
                        {
                            int n = ReadPlot(currpos, node);
                            currpos += n;
                        }
                        if (temp2.Value.ToLower() == "bool")
                        {
                            int n = ReadBool(currpos, node);
                            currpos += n;
                        }
                        if (temp2.Value.ToLower() == "function")
                        {
                            int n = ReadFunc(currpos, node);
                            currpos += n;
                        }
                        if (temp2.Value.ToLower() == "false")
                        {
                            TreeNode t = new TreeNode("false");
                            node.Nodes.Add(t);
                            currpos++;
                        }
                        if (temp2.Value.Length > 1)
                        {
                            if (temp2.Value[0] == 'a' && (isDigit(temp2.Value[1]) || temp2.Value[1] == '-'))
                            {
                                TreeNode t = new TreeNode("value_a");
                                string v = temp2.Value.Substring(1, temp2.Value.Length - 1);
                                t.Nodes.Add(Convert.ToInt32(v).ToString());
                                node.Nodes.Add(t);
                                currpos++;
                            }
                            if (temp2.Value[0] == 'i' && (isDigit(temp2.Value[1]) || temp2.Value[1] == '-'))
                            {
                                TreeNode t = new TreeNode("value_i");
                                string v = temp2.Value.Substring(1, temp2.Value.Length - 1);
                                t.Nodes.Add(Convert.ToInt32(v).ToString());
                                node.Nodes.Add(t);
                                currpos++;
                            }
                            if (temp2.Value[0] == 'f' && (isDigit(temp2.Value[1]) || temp2.Value[1] == '-'))
                            {
                                TreeNode t = new TreeNode("value_f");
                                string v = temp2.Value.Substring(1, temp2.Value.Length - 1);
                                t.Nodes.Add(Convert.ToSingle(v).ToString());
                                node.Nodes.Add(t);
                                currpos++;
                            }
                        }
                        break;
                    case 2:
                        TreeNode tn = new TreeNode("value");
                        tn.Nodes.Add(temp2.Value);
                        node.Nodes.Add(tn);
                        currpos++;
                        break;
                    case 3:
                    case 4:
                        if (temp2.Value == "(")
                        {
                            TreeNode t = new TreeNode("expr");
                            node.Nodes.Add(t);
                            int n = ReadExpression(currpos, t);
                            currpos += n;
                            break;
                        }
                        if (temp2.Value == "-")
                        {
                            Token t2 = tokenlist[currpos + 1];
                            if (t2.type == 2)
                            {
                                TreeNode t = new TreeNode("value");
                                t.Nodes.Add("-" + t2.Value);
                                node.Nodes.Add(t);
                                currpos += 2;
                            }
                            break;
                        } TreeNode tmp = new TreeNode(temp2.Value);
                        node.Nodes.Add(tmp);
                        currpos++;
                        break;
                }
                temp2 = tokenlist[currpos];
            }
            return currpos - pos + 1;
        }

        public int Parse(int pos, TreeNode node)
        {
            int len = tokenlist.Count;
            if (pos >= len - 1) return 0;
            Token temp = tokenlist[pos];
            switch (temp.type)
            {
                case 1://Word
                    if (temp.Value.ToLower() == "bool")
                    {
                        ReadBool(pos, node);
                        return 2;
                    }
                    if (temp.Value.ToLower() == "plot")
                    {
                        ReadPlot(pos, node);
                        return 2;
                    } break;
                case 2://Value
                    break;
                case 4://Symbol
                    if (temp.Value == "(")
                    {
                        TreeNode tmp = new TreeNode("expr");
                        node.Nodes.Add(tmp);
                        return ReadExpression(pos, tmp);
                    }
                    break;
                default:
                    return 0;
            }
            return 0;
        }

        public void Parser()
        {
            AST = new TreeNode("Root");
            Parse(0, AST);
            treeView1.Nodes.Clear();
            treeView1.Nodes.Add(AST);
            treeView1.ExpandAll();
            treeView1.Refresh();
        }

        public byte[] CodeBool(TreeNode node)
        {
            byte[] Cout = new byte[0];
            if (node.Text == "bool")
            {
                Cout = new byte[2];
                Cout[0] = 0;
                TreeNode t1 = node.Nodes[0];
                if (t1.Text == "true")
                    Cout[1] = 1;
            }
            return Cout;
        }

        public byte[] CodeIntValue(TreeNode node)
        {
            byte[] Cout = new byte[0];
            if (node.Text == "value" || node.Text == "value_a")
            {
                int n = Convert.ToInt16(node.Nodes[0].Text);
                Cout = new byte[5];
                Cout[0] = 0x31;
                byte[] buff = BitConverter.GetBytes(n);
                Cout[1] = buff[0];
                Cout[2] = buff[1];
                Cout[3] = buff[2];
                Cout[4] = buff[3];
            }
            return Cout;
        }

        public byte[] CodeIntValue_i(TreeNode node)
        {
            byte[] Cout = new byte[0];
            if (node.Text == "value_i")
            {
                int n = Convert.ToInt32(node.Nodes[0].Text);
                Cout = new byte[5];
                Cout[0] = 0x11;
                byte[] buff = BitConverter.GetBytes(n);
                Cout[1] = buff[0];
                Cout[2] = buff[1];
                Cout[3] = buff[2];
                Cout[4] = buff[3];
            }
            return Cout;
        }

        public byte[] CodeIntValue_f(TreeNode node)
        {
            byte[] Cout = new byte[0];
            if (node.Text == "value_f")
            {
                float f = Convert.ToSingle(node.Nodes[0].Text);
                Cout = new byte[5];
                Cout[0] = 0x22;
                byte[] buff = BitConverter.GetBytes(f);
                Cout[1] = buff[0];
                Cout[2] = buff[1];
                Cout[3] = buff[2];
                Cout[4] = buff[3];
            }
            return Cout;
        }


        public byte[] CodePlotBool(TreeNode node)
        {
            byte[] Cout = new byte[0];
            if (node.Text == "plot bool")
            {
                int n = Convert.ToInt16(node.Nodes[0].Text);
                Cout = new byte[5];
                Cout[0] = 0x60;
                byte[] buff = BitConverter.GetBytes(n);
                Cout[1] = buff[0];
                Cout[2] = buff[1];
                Cout[3] = buff[2];
                Cout[4] = buff[3];
            }
            return Cout;
        }

        public byte[] CodePlotInt(TreeNode node)
        {
            byte[] Cout = new byte[0];
            if (node.Text == "plot int")
            {
                int n = Convert.ToInt16(node.Nodes[0].Text);
                Cout = new byte[5];
                Cout[0] = 0x61;
                byte[] buff = BitConverter.GetBytes(n);
                Cout[1] = buff[0];
                Cout[2] = buff[1];
                Cout[3] = buff[2];
                Cout[4] = buff[3];
            }
            return Cout;
        }

        public byte[] CodePlotFloat(TreeNode node)
        {
            byte[] Cout = new byte[0];
            if (node.Text == "plot float")
            {
                int n = Convert.ToInt16(node.Nodes[0].Text);
                Cout = new byte[5];
                Cout[0] = 0x62;
                byte[] buff = BitConverter.GetBytes(n);
                Cout[1] = buff[0];
                Cout[2] = buff[1];
                Cout[3] = buff[2];
                Cout[4] = buff[3];
            }
            return Cout;
        }

        public byte[] CodeFunction(TreeNode node)
        {
            byte[] Cout = new byte[0];
            if (node.Text == "Function")
            {
                TreeNode t1 = node.Nodes[0];
                TreeNode t2 = node.Nodes[1];
                string s = t1.Text;
                short l = (short)s.Length;
                byte[] buff = BitConverter.GetBytes(l);
                Cout = new byte[9 + l];
                Cout[0] = 0x30;
                Cout[5] = buff[0];
                Cout[6] = buff[1];
                for (int i = 0; i < l; i++)
                    Cout[9 + i] = (byte)s[i];
            }
            return Cout;
        }

        public byte GetExprType(TreeNode node)
        {
            bool isFloat = false;
            TreeNode operation = node.Nodes[1];
            switch(operation.Text)
            {
                case "+":
                case "-":
                case "*":
                case "/":
                    for (int i = 0; i < (node.Nodes.Count + 1) / 2; i++)
                    {
                        TreeNode t = node.Nodes[i * 2];
                        switch (t.Text)
                        {
                            case "plot bool":
                                return 0;
                            case "plot float":
                            case "value_f":
                                isFloat = true;
                                break;
                            case "expr":
                                int res = GetExprType(t);
                                if (res == 0)
                                    return 0;
                                if (res == 2)
                                    isFloat = true;
                                break;
                        }
                    }
                    if (isFloat)
                        return 2;
                    else
                        return 1;
                default:
                    return 0;
            }
        }

        public byte[] CodeExpr(TreeNode node)
        {
            byte[] Cout = new byte[0];
            if (node.Text == "Root")
            {
                TreeNode t1 = node.Nodes[0];
                switch (t1.Text)
                {
                    case "expr":
                        Cout = CodeExpr(t1);
                        break;
                    case "bool":
                        Cout = CodeBool(t1);
                        break;
                    case "plot bool":
                        Cout = CodePlotBool(t1);
                        break;
                    case "plot int":
                        Cout = CodePlotInt(t1);
                        break;
                    case "plot float":
                        Cout = CodePlotFloat(t1);
                        break;
                }

            }
            if (node.Text == "expr")
            {
                int n = node.Nodes.Count;
                if (n < 3) return Cout;
                string compare = node.Nodes[1].Text;
                List<byte[]> Ctmp = new List<byte[]>();
                for (int i = 0; i < (n - 1) / 2; i++)
                {
                    switch (node.Nodes[i * 2].Text)
                    {
                        case "plot bool":
                            Ctmp.Add(CodePlotBool(node.Nodes[i * 2]));
                            break;
                        case "plot int":
                            Ctmp.Add(CodePlotInt(node.Nodes[i * 2]));
                            break;
                        case "plot float":
                            Ctmp.Add(CodePlotFloat(node.Nodes[i * 2]));
                            break;
                        case "expr":
                            Ctmp.Add(CodeExpr(node.Nodes[i * 2]));
                            break;
                        case "bool":
                            Ctmp.Add(CodeBool(node.Nodes[i * 2]));
                            break;
                        case "value":
                        case "value_a":
                            Ctmp.Add(CodeIntValue(node.Nodes[i * 2]));
                            break;
                        case "value_i":
                            Ctmp.Add(CodeIntValue_i(node.Nodes[i * 2]));
                            break;
                        case "value_f":
                            Ctmp.Add(CodeIntValue_f(node.Nodes[i * 2]));
                            break;
                        case "Function":
                            Ctmp.Add(CodeFunction(node.Nodes[i * 2]));
                            break;
                    }
                    if (node.Nodes[i * 2 + 1].Text != compare)
                        return Cout;
                }
                bool negexp = false;
                switch (node.Nodes[n - 1].Text)
                {
                    case "plot bool":
                        Ctmp.Add(CodePlotBool(node.Nodes[n - 1]));
                        break;
                    case "plot int":
                        Ctmp.Add(CodePlotInt(node.Nodes[n - 1]));
                        break;
                    case "plot float":
                        Ctmp.Add(CodePlotFloat(node.Nodes[n - 1]));
                        break;
                    case "expr":
                        Ctmp.Add(CodeExpr(node.Nodes[n - 1]));
                        break;
                    case "bool":
                        Ctmp.Add(CodeBool(node.Nodes[n - 1]));
                        break;
                    case "value":
                    case "value_a":
                        Ctmp.Add(CodeIntValue(node.Nodes[n - 1]));
                        break;
                    case "value_i":
                        Ctmp.Add(CodeIntValue_i(node.Nodes[n - 1]));
                        break;
                    case "value_f":
                        Ctmp.Add(CodeIntValue_f(node.Nodes[n - 1]));
                        break;
                    case "false":
                        negexp = true;
                        break;
                }
                int size = Ctmp.Count * 2 + 4;
                for (int i = 0; i < Ctmp.Count; i++)
                    size += Ctmp[i].Length;
                Cout = new byte[size];
                Cout[0] = (byte)(0x50 + GetExprType(node));
                switch (compare)
                {
                    case "&&":
                        Cout[1] = 4;
                        break;
                    case "||":
                        Cout[1] = 5;
                        break;
                    case "==":
                        Cout[1] = 7;
                        break;
                    case "!=":
                        Cout[1] = 8;
                        break;
                    case "<":
                        Cout[1] = 9;
                        break;
                    case "<=":
                        Cout[1] = 10;
                        break;
                    case ">":
                        Cout[1] = 11;
                        break;
                    case ">=":
                        Cout[1] = 12;
                        break;
                }
                if (negexp)
                    Cout[1] = 6;
                short hsize = (short)(Ctmp.Count * 2);
                byte[] count = BitConverter.GetBytes((short)Ctmp.Count);
                Cout[2] = count[0];
                Cout[3] = count[1];
                for (int i = 0; i < Ctmp.Count; i++)
                {
                    byte[] buff = BitConverter.GetBytes(hsize);
                    Cout[i * 2 + 4] = buff[0];
                    Cout[i * 2 + 5] = buff[1];
                    hsize += (short)Ctmp[i].Length;
                }
                hsize = (short)(Ctmp.Count * 2 + 4);
                for (int i = 0; i < Ctmp.Count; i++)
                {
                    for (int j = 0; j < Ctmp[i].Length; j++)
                        Cout[hsize + j] = Ctmp[i][j];
                    hsize += (short)Ctmp[i].Length;
                }
                return Cout;
            }
            return Cout;
        }

        public void CodeGen()
        {
            Code = CodeExpr(AST);
            int x = 0;
            byte n;
            string ascii = "";
            string outs = "";
            for (int j = 0; j < Code.Length; j++)
            {
                x++;
                if (x == 17)
                {
                    x = 1;
                    outs += ascii + "\n" + j.ToString("X") + "\t";
                    ascii = "";
                }
                n = Code[j];
                if (n > 15)
                {
                    outs += n.ToString("X") + " ";
                }
                else
                {
                    outs += "0" + n.ToString("X") + " ";
                }
                if (n > 31)
                {
                    ascii += (char)n;
                }
                else
                {
                    ascii += ".";
                }
            }
            if (x == 16)
            {
                outs += ascii;
            }
            else
            {
                for (int j = 0; j < 16 - x; j++)
                    outs += "   ";
                outs += ascii;
            }
            rtb2.Text = "Code\n00\t" + outs;
        }

        public bool CheckArrNumb(string code, int pos, out int end)
        {
            string pat = "0123456789 ";
            bool f;
            char c;
            while (true)
            {
                end = pos;
                c = code[pos++];
                f = false;
                for (int i = 0; i < pat.Length; i++)
                    if (pat[i] == c)
                    {
                        f = true;
                        break;
                    }
                if (!f && c != ']')
                    return false;
                if (!f && c == ']')
                    return true;
                if (pos >= code.Length)
                    return false;
            }
        }

        public bool CheckBracketCount()
        {
            int crbo, crbc, ccbo, ccbc;
            crbo = crbc = ccbo = ccbc = 0;
            string code = rtb1.Text;
            code = code.Replace("\n", "");
            code = code.Replace("\r", "");
            code = code.ToLower();
            rtb1.Text = code;
            for (int i = 0; i < code.Length; i++)
            {
                switch (code[i])
                {
                    case '(':
                        crbo++;
                        break;
                    case ')':
                        crbc++;
                        break;
                    case '[':
                        ccbo++;
                        int end = i + 1;
                        if (!CheckArrNumb(code, i + 1, out end))
                        {
                            string s;
                            if (i > 10)
                                s = code.Substring(i - 10, 10 + end - i);
                            else
                                s = code.Substring(0, end);
                            MessageBox.Show("Invalid Array Indexing @ : " + i + "!\n" + s);
                            return false;
                        }
                        break;
                    case ']':
                        ccbc++;
                        break;
                    case '&':
                        if (code[i + 1] == '&')
                            i++;
                        else
                        {
                            string s;
                            if (i > 20)
                                s = code.Substring(i - 20, 20);
                            else
                                s = code.Substring(0, i + 1);
                            MessageBox.Show("Missing '&' sign @ :\n" + s);
                            return false;
                        }
                        break;
                    default:
                        break;
                }
            }
            if (crbo != crbc || ccbo != ccbc)
            {
                if (crbo > crbc)
                    MessageBox.Show("Missing bracket(s) : " + (crbo - crbc) + " ')' bracket(s) missing!");
                else if (crbo < crbc)
                    MessageBox.Show("Missing bracket(s) : " + (crbc - crbo) + " '(' bracket(s) missing!");
                if (ccbo > ccbc)
                    MessageBox.Show("Missing bracket(s) : " + (ccbo - ccbc) + " ']' bracket(s) missing!");
                else if (ccbo < ccbc)
                    MessageBox.Show("Missing bracket(s) : " + (ccbc - ccbo) + " '[' bracket(s) missing!");
                return false;
            }
            return true;
        }

        public bool Compile()
        {
            if (!CheckBracketCount())
                return false;
            if (!Tokenizer())
                return false;
            Parser();
            try
            {
                CodeGen();
            }
            catch (OverflowException e)
            {
                if (e.Message == "Value was either too large or too small for an Int16.")
                {
                    MessageBox.Show("The maximum value of a 16-bit Integer is 32,767"); 
                }
                else
                {
                    MessageBox.Show(e.Message);
                }
                treeView1.Nodes.Clear();
                return false;
            }
            return true;
        }

        public void compileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Compile();
        }

        public void loadCodeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog FileDialog1 = new OpenFileDialog();
            FileDialog1.Filter = "text files (*.txt)|*.txt";
            if (FileDialog1.ShowDialog() == DialogResult.OK)
                rtb1.LoadFile(FileDialog1.FileName);
        }

        public void saveCodeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog FileDialog1 = new SaveFileDialog();
            FileDialog1.Filter = "text files (*.txt)|*.txt";
            if (FileDialog1.ShowDialog() == DialogResult.OK)
                rtb1.SaveFile(FileDialog1.FileName);
        }

        public void saveBinaryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string path;
            SaveFileDialog FileDialog1 = new SaveFileDialog();
            FileDialog1.Filter = "bin files (*.bin)|*.bin";
            if (FileDialog1.ShowDialog() == DialogResult.OK)
            {
                path = FileDialog1.FileName;
                FileStream fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
                for (int i = 0; i < Code.Length; i++)
                    fileStream.WriteByte(Code[i]);
                fileStream.Close();
            }

        }

        public void replaceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Code.Length == 0) return;
            cond.ReplaceData(Code, currnr);
        }

        private void compileAndReplaceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!Compile())
                return;
            if (Code.Length == 0) return;
            cond.ReplaceData(Code, currnr);
        }
    }
}
