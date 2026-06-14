using System;
using System.Collections.Generic;
using System.Reflection;

namespace CycloneGames.DataTable
{
    public static class DataTableGeneratedTableCollector
    {
        public static Dictionary<Type, object> CreateTableMap(object tableSet)
        {
            if (tableSet == null)
            {
                throw new ArgumentNullException(nameof(tableSet));
            }

            PropertyInfo[] properties = tableSet.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
            Dictionary<Type, object> tableMap = new Dictionary<Type, object>(properties.Length);
            for (int i = 0; i < properties.Length; i++)
            {
                PropertyInfo property = properties[i];
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                object table = property.GetValue(tableSet);
                if (table == null)
                {
                    continue;
                }

                tableMap[property.PropertyType] = table;
            }

            return tableMap;
        }
    }
}
