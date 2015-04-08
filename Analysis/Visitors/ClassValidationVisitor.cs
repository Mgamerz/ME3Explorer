﻿using ME3Script.Analysis.Symbols;
using ME3Script.Compiling.Errors;
using ME3Script.Language.Tree;
using ME3Script.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ME3Script.Analysis.Visitors
{
    public class ClassValidationVisitor : IASTVisitor
    {
        private SymbolTable Symbols;
        private MessageLog Log;
        private bool Success;

        public ClassValidationVisitor(MessageLog log, SymbolTable symbols)
        {
            Log = log;
            Symbols = symbols;
            Success = false;
        }

        private bool Error(String msg, SourcePosition start = null, SourcePosition end = null)
        {
            Log.LogError(msg, start, end);
            Success = false;
            return false;
        }

        public bool VisitNode(Class node)
        {
            if (Symbols.SymbolExists(node.Name))
                return Error("A class named '" + node.Name + "' already exists!", node.StartPos, node.EndPos);

            Symbols.AddSymbol(node.Name, node);
            Symbols.PushScope(node.Name);

            ASTNode parent;
            if (!Symbols.TryGetSymbol(node.Parent.Name, out parent))
                Error("No parent class named '" + node.Parent.Name + "' found!", node.Parent.StartPos, node.Parent.EndPos);
            if (parent != null)
            {
                if (parent.Type != ASTNodeType.Class)
                    Error("Parent named '" + node.Parent.Name + "' is not a class!", node.Parent.StartPos, node.Parent.EndPos);
                else if ((parent as Class).Extends(node.Name))
                    Error("Extending from '" + node.Parent.Name + "' causes circular extension!", node.Parent.StartPos, node.Parent.EndPos);
                else
                    node.Parent = (Class)parent;
            }

            if (node.OuterClass != null)
            {
                ASTNode outer;
                if (!Symbols.TryGetSymbol(node.OuterClass.Name, out outer))
                    Error("No outer class named '" + node.OuterClass.Name + "' found!", node.OuterClass.StartPos, node.OuterClass.EndPos);
                if (outer != null)
                {
                    if (outer.Type != ASTNodeType.Class)
                        Error("Outer named '" + node.OuterClass.Name + "' is not a class!", node.OuterClass.StartPos, node.OuterClass.EndPos);
                    else
                        node.OuterClass = (Class)outer;
                }
            }

            // TODO(?) validate class specifiers more than the initial parsing?

            return Success;
        }


        public bool VisitNode(VariableDeclaration node)
        {
            throw new NotImplementedException();
        }

        public bool VisitNode(VariableType node)
        {
            throw new NotImplementedException();
        }

        public bool VisitNode(Struct node)
        {
            throw new NotImplementedException();
        }

        public bool VisitNode(Enumeration node)
        {
            throw new NotImplementedException();
        }

        public bool VisitNode(Function node)
        {
            throw new NotImplementedException();
        }

        public bool VisitNode(State node)
        {
            throw new NotImplementedException();
        }

        public bool VisitNode(OperatorDeclaration node)
        {
            throw new NotImplementedException();
        }
    }
}
