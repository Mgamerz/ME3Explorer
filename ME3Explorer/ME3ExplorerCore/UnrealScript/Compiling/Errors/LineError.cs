﻿using ME3ExplorerCore.UnrealScript.Utilities;

namespace ME3ExplorerCore.UnrealScript.Compiling.Errors
{
    public class LineError : PositionedMessage
    {
        public LineError(string msg, SourcePosition start, SourcePosition end)
            : base(msg, start, end) { }

        public override string ToString()
        {
            return "ERROR| Line " + Line + " |: " + Message; 
        }
    }
}