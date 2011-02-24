using System;
using System.Data;
using System.Collections.Generic;
using Aurora.Framework;
using OpenSim.Framework;
using OpenMetaverse;

namespace Aurora.DataManager
{
    public abstract class DataManagerBase : IDataConnector
    {
        private const string VERSION_TABLE_NAME = "aurora_migrator_version";
        private const string COLUMN_NAME = "name";
        private const string COLUMN_VERSION = "version";

        #region IGenericData Members

        public abstract string Identifier { get; }
        public abstract void ConnectToDatabase(string connectionString, string migratorName);
        public abstract List<string> Query(string keyRow, object keyValue, string table, string wantedValue, string Order);
        public abstract List<string> Query(string whereClause, string table, string wantedValue);
        public abstract List<string> QueryFullData(string whereClause, string table, string wantedValue);
        public abstract IDataReader QueryDataFull(string whereClause, string table, string wantedValue);
        public abstract IDataReader QueryData(string whereClause, string table, string wantedValue);
        public abstract List<string> Query(string keyRow, object keyValue, string table, string wantedValue);
        public abstract List<string> Query(string[] keyRow, object[] keyValue, string table, string wantedValue);
        public abstract bool Insert(string table, object[] values);
        public abstract bool Insert(string table, string[] keys, object[] values);
        public abstract IDataReader QueryReader(string keyRow, object keyValue, string table, string wantedValue);
        public abstract bool Delete(string table, string[] keys, object[] values);
        public abstract bool Delete(string table, string whereclause);
        public abstract bool DeleteByTime(string table, string key);
        public abstract bool Insert(string table, object[] values, string updateKey, object updateValue);
        public abstract bool Update(string table, object[] setValues, string[] setRows, string[] keyRows, object[] keyValues);
        public abstract void CloseDatabase();
        public abstract bool TableExists(string table);
        public abstract void CreateTable(string table, ColumnDefinition[] columns);
        public abstract void UpdateTable(string table, ColumnDefinition[] columns);
        public abstract bool Replace(string table, string[] keys, object[] values);
        public abstract bool DirectReplace(string table, string[] keys, object[] values);
        public abstract IGenericData Copy();
        public abstract string GetColumnTypeStringSymbol(ColumnTypes type);
        public abstract void DropTable(string tableName);
        public abstract void ForceRenameTable(string oldTableName, string newTableName);
        public abstract string FormatDateTimeString(int time);

        public Version GetAuroraVersion(string migratorName)
        {
            if (!TableExists(VERSION_TABLE_NAME))
            {
                CreateTable(VERSION_TABLE_NAME, new[] {new ColumnDefinition {Name = COLUMN_VERSION, Type = ColumnTypes.String},
                new ColumnDefinition {Name = COLUMN_NAME, Type = ColumnTypes.String}});
            }

            List<string> results = Query(COLUMN_NAME, migratorName, VERSION_TABLE_NAME, COLUMN_VERSION);
            if (results.Count > 0)
            {
                Version highestVersion = null;
                foreach (string result in results)
                {
                    if (result.Trim() == string.Empty)
                    {
                        continue;
                    }
                    var version = new Version(result);
                    if (highestVersion == null || version > highestVersion)
                    {
                        highestVersion = version;
                    }
                }
                return highestVersion;
            }

            return null;
        }

        public void WriteAuroraVersion(Version version, string MigrationName)
        {
            if (!TableExists(VERSION_TABLE_NAME))
            {
                CreateTable(VERSION_TABLE_NAME, new[] {new ColumnDefinition {Name = COLUMN_VERSION, Type = ColumnTypes.String100}});
            }
            //Remove previous versions
            Delete(VERSION_TABLE_NAME, new string[1] { COLUMN_NAME }, new object[1] { MigrationName });
            //Add the new version
            Insert(VERSION_TABLE_NAME, new[] { version.ToString(), MigrationName });
        }

        public void CopyTableToTable(string sourceTableName, string destinationTableName, ColumnDefinition[] columnDefinitions)
        {
            if (!TableExists(sourceTableName))
            {
                throw new MigrationOperationException("Cannot copy table to new name, source table does not exist: " + sourceTableName);
            }

            if (TableExists(destinationTableName))
            {
                throw new MigrationOperationException("Cannot copy table to new name, table with same name already exists: " + destinationTableName);
            }

            if (!VerifyTableExists(sourceTableName, columnDefinitions))
            {
                throw new MigrationOperationException("Cannot copy table to new name, source table does not match columnDefinitions: " + destinationTableName);
            }

            EnsureTableExists(destinationTableName, columnDefinitions);
            CopyAllDataBetweenMatchingTables(sourceTableName, destinationTableName, columnDefinitions);
        }

        public bool VerifyTableExists(string tableName, ColumnDefinition[] columnDefinitions)
        {
            if (!TableExists(tableName))
            {
                MainConsole.Instance.Output("[DataMigrator]: Issue finding table " + tableName + " when verifing tables exist!", "Warn");
                return false;
            }

            List<ColumnDefinition> extractedColumns = ExtractColumnsFromTable(tableName);
            foreach (ColumnDefinition columnDefinition in columnDefinitions)
            {
                if (!extractedColumns.Contains(columnDefinition))
                {
                    ColumnDefinition thisDef = null;
                    //Check to see whether the two tables have the same type, but under different names
                    foreach (ColumnDefinition extractedDefinition in extractedColumns)
                    {
                        if (extractedDefinition.Name == columnDefinition.Name)
                        {
                            thisDef = extractedDefinition;
                            break;
                        }
                    }
                    if (thisDef != null)
                    {
                        if (GetColumnTypeStringSymbol(thisDef.Type) == GetColumnTypeStringSymbol(columnDefinition.Type))
                            continue; //They are the same type, let them go on through
                    }
                    MainConsole.Instance.Output("[DataMigrator]: Issue verifing table " + tableName + " column " + columnDefinition.Name + " when verifing tables exist!", "Warn");
                    return false;
                }
            }

            return true;
        }

        public void EnsureTableExists(string tableName, ColumnDefinition[] columnDefinitions)
        {
            if (TableExists(tableName))
            {
                if (!VerifyTableExists(tableName, columnDefinitions))
                {
                    //throw new MigrationOperationException("Cannot create, table with same name and different columns already exists. This should be fixed in a migration: " + tableName);
                    UpdateTable(tableName, columnDefinitions);
                }
                return;
            }

            CreateTable(tableName, columnDefinitions);
        }

        public void RenameTable(string oldTableName, string newTableName)
        {
            //Make sure that the old one exists and the new one doesn't
            if (TableExists(oldTableName) && !TableExists(newTableName))
            {
                ForceRenameTable(oldTableName, newTableName);
            }
        }

        #endregion

        protected abstract void CopyAllDataBetweenMatchingTables(string sourceTableName, string destinationTableName, ColumnDefinition[] columnDefinitions);
        protected abstract List<ColumnDefinition> ExtractColumnsFromTable(string tableName);
    }
}