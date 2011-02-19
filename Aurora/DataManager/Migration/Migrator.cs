using System;
using System.Collections.Generic;
using C5;
using Aurora.Framework;

namespace Aurora.DataManager.Migration
{
    public class Migrator : IRestorePoint
    {
        public List<Rec<string, ColumnDefinition[]>> schema;
        public Dictionary<string, string> renameSchema;

        public Version Version { get; protected set; }

        public bool CanProvideDefaults { get; protected set; }

        #region IRestorePoint Members

        public virtual void DoRestore(IDataConnector genericData)
        {
        }

        #endregion

        public bool Validate(IDataConnector genericData)
        {
            if (genericData.GetAuroraVersion() != Version)
            {
                return false;
            }
            return DoValidate(genericData);
        }

        protected virtual bool DoValidate(IDataConnector genericData)
        {
            return true;
        }

        public IRestorePoint PrepareRestorePoint(IDataConnector genericData)
        {
            DoPrepareRestorePoint(genericData);
            return this;
        }

        protected virtual void DoPrepareRestorePoint(IDataConnector genericData)
        {

        }

        public void Migrate(IDataConnector genericData)
        {
            DoMigrate(genericData);
            genericData.WriteAuroraVersion(Version);
        }

        protected virtual void DoMigrate(IDataConnector genericData)
        {
        }

        public void CreateDefaults(IDataConnector genericData)
        {
            DoCreateDefaults(genericData);
            genericData.WriteAuroraVersion(Version);
        }

        protected virtual void DoCreateDefaults(IDataConnector genericData)
        {
        }

        protected ColumnDefinition[] ColDefs(params ColumnDefinition[] defs)
        {
            return defs;
        }

        protected ColumnDefinition ColDef(string name, ColumnTypes columnType)
        {
            return new ColumnDefinition() { Name = name, Type = columnType, IsPrimary = false };
        }

        protected ColumnDefinition ColDef(string name, ColumnTypes columnType, bool isPrimary)
        {
            return new ColumnDefinition() { Name = name, Type = columnType, IsPrimary = isPrimary };
        }

        protected void AddSchema(string table, ColumnDefinition[] definitions)
        {
            schema.Add(new Rec<string, ColumnDefinition[]>(table, definitions));
        }

        protected void RemoveSchema(string table)
        {
            //Remove all of the tables that have this name
            schema.RemoveAll(delegate(Rec<string, ColumnDefinition[]> r)
            {
                if (r.X1 == table)
                    return true;
                return false;
            });
        }

        protected void EnsureAllTablesInSchemaExist(IDataConnector genericData)
        {
            foreach (System.Collections.Generic.KeyValuePair<string, string> r in renameSchema)
            {
                genericData.RenameTable(r.Key, r.Value);
            }
            foreach (var s in schema)
            {
                genericData.EnsureTableExists(s.X1, s.X2);
            }
        }

        protected bool TestThatAllTablesValidate(IDataConnector genericData)
        {
            foreach (var s in schema)
            {
                if (!genericData.VerifyTableExists(s.X1, s.X2))
                {
                    return false;
                }
            }
            return true;
        }

        protected void CopyAllTablesToTempVersions(IDataConnector genericData)
        {
            foreach (var s in schema)
            {
                CopyTableToTempVersion(genericData, s.X1, s.X2);
            }
        }

        protected void RestoreTempTablesToReal(IDataConnector genericData)
        {
            foreach (var s in schema)
            {
                RestoreTempTableToReal(genericData, s.X1, s.X2);
            }
        }

        private void CopyTableToTempVersion(IDataConnector genericData, string tablename, ColumnDefinition[] columnDefinitions)
        {
            genericData.CopyTableToTable(tablename, GetTempTableNameFromTableName(tablename), columnDefinitions);
        }

        private string GetTempTableNameFromTableName(string tablename)
        {
            return tablename + "_temp";
        }

        private void RestoreTempTableToReal(IDataConnector genericData, string tablename, ColumnDefinition[] columnDefinitions)
        {
            genericData.CopyTableToTable(GetTempTableNameFromTableName(GetTempTableNameFromTableName(tablename)), tablename, columnDefinitions);
        }

        public void ClearRestorePoint(IDataConnector genericData)
        {
            foreach (var s in schema)
            {
                DeleteTempVersion(genericData, s.X1);
            }
        }

        private void DeleteTempVersion(IDataConnector genericData, string tableName)
        {
            string tempTableName = GetTempTableNameFromTableName(tableName);
            if (genericData.TableExists(tempTableName))
            {
                genericData.DropTable(tempTableName);   
            }
        }
    }
}