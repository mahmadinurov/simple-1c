﻿using System.Text;

namespace Simple1C.Impl.Sql.SqlAccess.Syntax
{
    internal class SelectColumn : ISqlElement
    {
        public ISqlElement Expression { get; set; }
        public string Alias { get; set; }

        public void WriteTo(StringBuilder b)
        {
            Expression.WriteTo(b);
            SqlHelpers.WriteAlias(b, Alias);
        }
    }
}