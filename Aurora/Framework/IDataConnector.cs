using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using OpenMetaverse;
using OpenSim.Framework;
using Nini.Config;

namespace Aurora.Framework
{
    /// <summary>
    /// Connector that links Aurora IDataPlugins to a database backend
    /// </summary>
    public interface IDataConnector : IGenericData
    {
        /// <summary>
        /// Checks to see if table 'table' exists
        /// </summary>
        /// <param name="table"></param>
        /// <returns></returns>
        bool TableExists(string table);

        /// <summary>
        /// Create a generic table
        /// </summary>
        /// <param name="table"></param>
        /// <param name="columns"></param>
        void CreateTable(string table, ColumnDefinition[] columns);

        /// <summary>
        /// Get the latest version of the database
        /// </summary>
        /// <returns></returns>
        Version GetAuroraVersion(string migratorName);

        /// <summary>
        /// Set the version of the database
        /// </summary>
        /// <param name="version"></param>
        void WriteAuroraVersion(Version version, string MigrationName);

        /// <summary>
        /// Copy tables
        /// </summary>
        /// <param name="sourceTableName"></param>
        /// <param name="destinationTableName"></param>
        /// <param name="columnDefinitions"></param>
        void CopyTableToTable(string sourceTableName, string destinationTableName, ColumnDefinition[] columnDefinitions);
        
        /// <summary>
        /// Check whether the data table exists and that the columns are correct
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="columnDefinitions"></param>
        /// <returns></returns>
        bool VerifyTableExists(string tableName, ColumnDefinition[] columnDefinitions);
        
        /// <summary>
        /// Check whether the data table exists and that the columns are correct
        /// Then create the table if it is not created
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="columnDefinitions"></param>
        void EnsureTableExists(string tableName, ColumnDefinition[] columnDefinitions);

        /// <summary>
        /// Rename the table from oldTableName to newTableName
        /// </summary>
        /// <param name="oldTableName"></param>
        /// <param name="newTableName"></param>
        void RenameTable(string oldTableName, string newTableName);
        
        /// <summary>
        /// Drop a table
        /// </summary>
        /// <param name="tableName"></param>
        void DropTable(string tableName);

        /// <summary>
        /// Name of the module
        /// </summary>
        string Identifier { get; }
    }

    public enum DataManagerTechnology
    {
        SQLite,
        MySql,
        MSSQL2008,
        MSSQL7
    }

    public enum ColumnTypes
    {
        Blob,
        LongBlob,
        Char36,
        Char32,
        Date,
        DateTime,
        Integer11,
        Integer30,
        String,
        String1,
        String2,
        String16,
        String32,
        String36,
        String45,
        String50,
        String64,
        String128,
        String100,
        String255,
        String512,
        String1024,
        String8196,
        Text,
        MediumText,
        LongText
    }
    public class ColumnDefinition
    {
        public string Name { get; set; }
        public ColumnTypes Type { get; set; }
        public bool IsPrimary { get; set; }

        public override bool Equals(object obj)
        {
            var cdef = obj as ColumnDefinition;
            if (cdef != null)
            {
                return cdef.Name == Name && cdef.Type == Type && cdef.IsPrimary == IsPrimary;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }
}
