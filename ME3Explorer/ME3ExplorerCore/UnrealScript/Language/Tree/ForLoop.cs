﻿using System.Collections.Generic;
using ME3ExplorerCore.UnrealScript.Analysis.Visitors;
using ME3ExplorerCore.UnrealScript.Utilities;

namespace ME3ExplorerCore.UnrealScript.Language.Tree
{
    public class ForLoop : Statement
    {
        public Expression Condition;
        public CodeBody Body;
        public Statement Init;
        public Statement Update;

        public ForLoop(Statement init, Expression cond, Statement update,
                       CodeBody body,
                       SourcePosition start = null, SourcePosition end = null)
            : base(ASTNodeType.WhileLoop, start, end)
        {
            Condition = cond;
            Body = body;
            Init = init;
            Update = update;
            if (init is not null) init.Outer = this;
            if (cond is not null) cond.Outer = this;
            if (update is not null) update.Outer = this;
            body.Outer = this;
        }

        public override bool AcceptVisitor(IASTVisitor visitor)
        {
            return visitor.VisitNode(this);
        }
        public override IEnumerable<ASTNode> ChildNodes
        {
            get
            {
                if (Init != null) yield return Init;
                if (Condition != null) yield return Condition;
                if (Update != null) yield return Update;
                if (Body != null) yield return Body;
            }
        }
    }
}