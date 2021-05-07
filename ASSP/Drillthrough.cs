/*============================================================================
  File:    Drillthrough.cs

  Summary: Implements stored procedures that aid in performing custom drillthrough.

  Date:    August 28, 2009

  ----------------------------------------------------------------------------
  This file is part of the Analysis Services Stored Procedure Project.
  http://www.codeplex.com/Wiki/View.aspx?ProjectName=ASStoredProcedures
  
  THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY
  KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE
  IMPLIED WARRANTIES OF MERCHANTABILITY AND/OR FITNESS FOR A
  PARTICULAR PURPOSE.
============================================================================*/

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AnalysisServices.AdomdServer;
using AdomdClient = Microsoft.AnalysisServices.AdomdClient;
using System.Data;
using System.Data.Odbc;
using Tuple = Microsoft.AnalysisServices.AdomdServer.Tuple;
using System.Text.RegularExpressions; //resolves ambiguous reference in .NET 4 with System.Tuple


namespace ASStoredProcs
{

    public class Drillthrough
    {

        //in the following functions, if maxrows is not specified, it uses the DefaultDrillthroughMaxRows server setting

        #region GetDefaultDrillthroughMDX overloads
        public static string GetDefaultDrillthroughMDX()
        {
            return GetDrillthroughMDXInternal(null, null, null);
        }

        // Implied casting seems to cause an issue here where passing in a tuple 
        // reference gets cast to a scalar value which then treats 0 as false and non-zero as true
        //public static string GetDefaultDrillthroughMDX(bool skipDefaultMembers)
        //{
        //    return GetDrillthroughMDXInternal(null, null, null,skipDefaultMembers);
        //}

        public static string GetDefaultDrillthroughMDX(Tuple tuple)
        {
            return GetDrillthroughMDXInternal(tuple, null, null);
        }

        public static string GetDefaultDrillthroughMDX(Tuple tuple, int iMaxRows)
        {
            return GetDrillthroughMDXInternal(tuple, null, iMaxRows);
        }

        public static string GetDefaultDrillthroughMDX(Tuple tuple, int iMaxRows, bool skipDefaultMembers)
        {
            return GetDrillthroughMDXInternal(tuple, null, iMaxRows, skipDefaultMembers);
        }
        #endregion

        #region GetCustomDrillthroughMDX overloads
        public static string GetCustomDrillthroughMDX(string sReturnColumns)
        {
            return GetDrillthroughMDXInternal(null, sReturnColumns, null);
        }

        public static string GetCustomDrillthroughMDX(string sReturnColumns, Tuple tuple)
        {
            return GetDrillthroughMDXInternal(tuple, sReturnColumns, null);
        }

        public static string GetCustomDrillthroughMDX(string sReturnColumns, Tuple tuple, int iMaxRows)
        {
            return GetDrillthroughMDXInternal(tuple, sReturnColumns, iMaxRows);
        }

        public static string GetCustomDrillthroughMDX64(string sReturnColumns)
        {
            return Drillthrough.GetDrillthroughMDXInternal64((Tuple)null, true).Replace("\"", "&quot;").Replace("'", "&apos;");
        }

        public static string GetCustomDrillthroughMDX(string sReturnColumns, Tuple tuple, int iMaxRows, bool skipDefaultMembers)
        {
            return GetDrillthroughMDXInternal(tuple, sReturnColumns, iMaxRows, skipDefaultMembers);
        }

        private static string GetDrillthroughMDXInternal(Tuple tuple, string sReturnColumns, int? iMaxRows)
        {
            return GetDrillthroughMDXInternal(tuple, sReturnColumns, iMaxRows, true);
        }
        #endregion


        private static string GetDrillthroughMDXInternal(Tuple tuple, string sReturnColumns, int? iMaxRows, bool skipDefaultMembers)
        {
            if (sReturnColumns != null)
            {
                //passed in a set of return columns
                return "drillthrough " + (iMaxRows == null ? "" : "maxrows " + iMaxRows) + " select (" + CurrentCellAttributes(tuple, skipDefaultMembers) + ") on 0 from [" + AMOHelpers.GetCurrentCubeName() + "] return " + sReturnColumns;
            }
            else
            {
                //passed in a reference to a measure, so just do the default drillthrough for it
                return "drillthrough " + (iMaxRows == null ? "" : "maxrows " + iMaxRows) + " select (" + CurrentCellAttributes(tuple, skipDefaultMembers) + ") on 0 from [" + AMOHelpers.GetCurrentCubeName() + "]";
            }
        }

        private static string GetDrillthroughMDXInternal64(Tuple tuple, bool skipDefaultMembers)
        {
            string s = CurrentCellAttributes(tuple, skipDefaultMembers);
            string pattern = @"^(\(){1}(.*?)(\)){1}$";
            return Regex.Replace(s, pattern, "$2").Replace(",", ",\r\n");

        }

        public static DataTable ExecuteDrillthroughAndFixColumns(string sDrillthroughMDX)
        {
            AdomdClient.AdomdConnection conn = TimeoutUtility.ConnectAdomdClient("Data Source=" + Context.CurrentServerID + ";Initial Catalog=" + Context.CurrentDatabaseName + ";Application Name=ASSP;");
            try
            {
                AdomdClient.AdomdCommand cmd = new AdomdClient.AdomdCommand();
                cmd.Connection = conn;
                cmd.CommandText = sDrillthroughMDX;
                DataTable tbl = new DataTable();
                AdomdClient.AdomdDataAdapter adp = new AdomdClient.AdomdDataAdapter(cmd);
                TimeoutUtility.FillAdomdDataAdapter(adp, tbl);

                Dictionary<string, int> dictColumnNames = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);

                foreach (DataColumn col in tbl.Columns)
                {
                    string sNewColumnName = col.ColumnName.Substring(col.ColumnName.LastIndexOf('.') + 1).Replace("[", "").Replace("]", "");
                    if (dictColumnNames.ContainsKey(sNewColumnName))
                        dictColumnNames[sNewColumnName]++;
                    else
                        dictColumnNames.Add(sNewColumnName, 1);
                }

                foreach (DataColumn col in tbl.Columns)
                {
                    string sNewColumnName = col.ColumnName.Substring(col.ColumnName.LastIndexOf('.') + 1).Replace("[", "").Replace("]", "");
                    if (dictColumnNames[sNewColumnName] > 1)
                        sNewColumnName = col.ColumnName.Substring(col.ColumnName.LastIndexOf('[') + 1).Replace("[", "").Replace("]", "").Replace("$", "");
                    if (!tbl.Columns.Contains(sNewColumnName))
                        col.ColumnName = sNewColumnName;
                }

                return tbl;
            }
            finally
            {
                conn.Close();
            }
        }

        public static DataTable ExecuteDrillthroughAndFixColumns(string ConnectionString, string ConfigTable, string sDrillthroughMDX64, short ConfigId = 1)
        {
            Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns started for User: {0}", Context.CurrentConnection.User.Name));
            OdbcConnection connection = (OdbcConnection)null;
            string input = sDrillthroughMDX64.Replace("&quot;", "\"").Replace("&apos;", "'");
            Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns input: {0}", input));
            DataTable dataTable1 = new DataTable();
            DataTable dataTable2 = new DataTable();
            DataTable dataTable3 = new DataTable();
            short num1 = (short)ConfigId;
            string str1 = "";
            string str2 = "";
            string newValue1 = " 1=1 ";
            bool flag = true;
            Context.TraceEvent(999, 0, "ExecuteDrillthroughAndFixColumns Connect to DB");
            try
            {
                connection = new OdbcConnection(ConnectionString);
                connection.Open();
                OdbcDataAdapter tdDataAdapter = new OdbcDataAdapter();
                Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns ConnectionString: {0}", ConnectionString));
                Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns ConfigTable: {0}", ConfigTable));
                Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns sDrillthroughMDX64: {0}", sDrillthroughMDX64));

                OdbcCommand cmd = new OdbcCommand("insert into initial_param (ConnectionString, ConfigTable, sDrillthroughMDX64) values (? , ? , ? )", connection);
                cmd.Parameters.Add("ConnectionString", OdbcType.VarChar, 500).Value = ConnectionString;
                cmd.Parameters.Add("ConfigTable", OdbcType.VarChar, 100).Value = ConfigTable;
                cmd.Parameters.Add("sDrillthroughMDX64", OdbcType.VarChar, 10000).Value = sDrillthroughMDX64;
                cmd.ExecuteNonQuery();

                tdDataAdapter.SelectCommand = new OdbcCommand("select * from " + ConfigTable + " where configid = ?", connection);
                tdDataAdapter.SelectCommand.Parameters.Add("configid", OdbcType.Int).Value = num1;

                tdDataAdapter.Fill(dataTable3);
                string str3 = dataTable3.Rows[0]["MappingTable"].ToString();
                string str4 = dataTable3.Rows[0]["DetailSql"].ToString();
                string newValue2 = dataTable3.Rows[0]["MaxRows"].ToString();
                string str5 = dataTable3.Rows[0]["TimeOut"].ToString();

                Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns MappingTable: {0}", str3));
                Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns DetailSql: {0}", str4));
                Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns MaxRows: {0}", newValue2));
                Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns TimeOut: {0}", str5));

                OdbcCommand tdCommand1 = new OdbcCommand("select * from " + str3 + " where ConfigId = " + num1.ToString() + " order by USER_NAME_FIELD_ORDER ", connection);
                Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns Command SQL: {0}", tdCommand1.CommandText));
                tdCommand1.CommandTimeout = (int)Convert.ToInt16(str5);
                tdDataAdapter.SelectCommand = tdCommand1;
                tdDataAdapter.Fill(dataTable1);
                string[] strArray1 = System.Text.RegularExpressions.Regex.Split(input, @",.*\n");
                Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns Find Dimensions: {0}", strArray1.Length.ToString()));
                tdCommand1.Parameters.Clear();
                foreach (string str6 in strArray1)
                {
                    //Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns Process fields: {0}", str6));
                    //Context.TraceEvent(999, 0, string.Format("row: {0} Index Of & {1}", str6, str6.IndexOf("&")));
                    if (str6.IndexOf("&") >= 0 || flag)
                    {

                        foreach (DataRow dataRow in (InternalDataCollectionBase)dataTable1.Rows)
                        {
                            if (flag && dataRow["USER_NAME_FIELD"].ToString().Length != 0)
                            {
                                str1 = str1 + dataRow["DATABASE_FIELD"] + " as \"" + (string)dataRow["USER_NAME_FIELD"] + "\" ,";
                                //Context.TraceEvent(999, 0, string.Format("str1 value: {0}", str1));
                            }

                            if (str6.IndexOf("&") >= 0 && str6.IndexOf(dataRow["DIMENSION_FIELD"].ToString().Trim()) >= 0)
                            {
                                newValue1 = string.Concat(new object[4]
                                {
                                  (object) newValue1,
                                  (object) " AND ",
                                  dataRow["DATABASE_FIELD"],
                                  (object) " = ? "
                                });
                                //Context.TraceEvent(999, 0, string.Format("str6 value: {0} str6.IndexOf&:{1} str6.IndexOf]:{2}", str1, str6.IndexOf("&"), str6.IndexOf("]", str6.IndexOf("&") + 2)));
                                string str7 = str6.Substring(str6.IndexOf("&") + 2, str6.IndexOf("]", str6.IndexOf("&") + 2) - str6.IndexOf("&") - 2);
                                //Context.TraceEvent(999, 0, string.Format("str7 value: {0}", str7));
                                switch (dataRow["DATABASE_FIELD_TYPE"].ToString())
                                {
                                    case "VARCHAR":
                                        tdCommand1.Parameters.Add(dataRow["FIELD_ID"].ToString(), OdbcType.NVarChar, str7.Length).Value = (object)str7;
                                        break;
                                    case "INT":
                                        tdCommand1.Parameters.Add(dataRow["FIELD_ID"].ToString(), OdbcType.Int, str7.Length).Value = (object)str7;
                                        break;
                                    case "SMALLINT":
                                        tdCommand1.Parameters.Add(dataRow["FIELD_ID"].ToString(), OdbcType.SmallInt, str7.Length).Value = (object)str7;
                                        break;
                                    case "DATE":
                                        tdCommand1.Parameters.Add(dataRow["FIELD_ID"].ToString(), OdbcType.Date, str7.Length).Value = DateTime.ParseExact(str7.Substring(0, 10), "yyyy-MM-dd", null);
                                        str7 = str7.Substring(0, 10);
                                        break;
                                    default:
                                        tdCommand1.Parameters.Add(dataRow["FIELD_ID"].ToString(), OdbcType.NVarChar, str7.Length).Value = (object)str7;
                                        break;
                                }
                                //tdCommand1.Parameters[dataRow["FIELD_ID"].ToString()].Value = (object) str7;
                                str2 = str2 + (object)" AND " + (string)dataRow["DATABASE_FIELD"] + " = '" + str7 + "'";
                                //Context.TraceEvent(999, 0, string.Format("str2 value: {0}", str2));
                            }
                        }
                        flag = false;
                    }
                }
                tdCommand1.CommandText = str4.Replace("<COLUMN_LIST>", str1.TrimEnd(new char[1]
                {
          ','
                })).Replace("<WHERE>", newValue1).Replace("<MAXROWS>", newValue2);
                Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns Prepared SQL query: {0}", tdCommand1.CommandText));

                OdbcCommand cmd1 = new OdbcCommand("Insert Into SQLQueryLog (SQLTxt, UserName ) Values (?, ?)", connection);
                cmd1.Parameters.Add("SQLtxt", OdbcType.VarChar, 16000).Value = (str2.Length > 1) ? str2.Substring(0, (str2.Length > 16000) ? 16000 : str2.Length) : "NULL";
                cmd1.Parameters.Add("UserName", OdbcType.VarChar, 100).Value = Context.CurrentConnection.User.Name;
                cmd1.ExecuteNonQuery();

                OdbcCommand OdbcCommand2 = new OdbcCommand("select top 1 query_id from SQLQueryLog where event_time = (select max (event_time ) from SQLQueryLog ); ", connection);
                tdDataAdapter.SelectCommand = OdbcCommand2;
                DataTable dataTable4 = new DataTable();
                tdDataAdapter.Fill(dataTable4);
                long num2 = Convert.ToInt64(dataTable4.Rows[0][0].ToString());
                DateTime now1 = DateTime.Now;
                Context.TraceEvent(999, 0, "ExecuteDrillthroughAndFixColumns Start excecute SQL query");
                tdDataAdapter.SelectCommand = tdCommand1;
                tdDataAdapter.Fill(dataTable2);
                Context.TraceEvent(999, 0, "ExecuteDrillthroughAndFixColumns End excecute SQL query");
                DateTime now2 = DateTime.Now;

                TimeSpan timeSpan = now2 - now1;
                string str8 = timeSpan.TotalSeconds.ToString();
                Context.TraceEvent(999, 0, string.Format("Work completed in {0} seconds", str8));

                OdbcCommand cmd3 = new OdbcCommand("insert INTO SQLQueryLog_dur (QUERY_ID , duration , quantity ) VALUES (?, ?, ?)", connection);
                cmd3.Parameters.Add("QUERY_ID", OdbcType.Int).Value = num2;
                cmd3.Parameters.Add("duration", OdbcType.Double).Value = Convert.ToInt64(timeSpan.TotalSeconds);
                cmd3.Parameters.Add("quantity", OdbcType.Int).Value = dataTable2.Rows.Count;
                cmd3.ExecuteNonQuery();

                return dataTable2;
            }
            catch (OdbcException ex)
            {
                try
                {
                    Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndFixColumns ERROR: {0}", ex.Message));
                    OdbcCommand cmd3 = new OdbcCommand("insert INTO SQLErrLog (Error_message) VALUES(?)", connection);
                    cmd3.Parameters.Add("Error_message", OdbcType.VarChar, 2000).Value = ex.Message;
                    cmd3.ExecuteNonQuery();
                }
                catch
                {
                }
                if (null != connection)
                    connection.Close();
                dataTable2.Clear();
                dataTable2.Columns.Add("Error String", typeof(string));
                dataTable2.Rows.Add(new object[1]
                {
          (object) ex.Message
                });
                return dataTable2;
            }
            finally
            {
                if (null != connection)
                    connection.Close();
            }
        }

        public static DataTable ExecuteDrillthroughAndTranslateColumns(string sDrillthroughMDX)
        {
            Regex columnNameRegex = new Regex(@"\[(?<cube>[^]]*)]\.\[(?<level>[^]]*)]", RegexOptions.Compiled);
            string connStr = "Data Source=" + Context.CurrentServerID + ";Initial Catalog=" + Context.CurrentDatabaseName + ";Application Name=ASSP;Locale Identifier=" + Context.CurrentConnection.ClientCulture.LCID;
            AdomdClient.AdomdConnection conn = TimeoutUtility.ConnectAdomdClient(connStr);
            Context.TraceEvent(999, 0, string.Format("ExecuteDrillthroughAndTranslateColumns ConnectionString: {0}", connStr));
            try
            {
                Dictionary<string, string> translations = new Dictionary<string, string>();
                // get level names
                var resColl = new AdomdClient.AdomdRestrictionCollection();
                resColl.Add("CUBE_SOURCE", "3"); // dimensions
                resColl.Add("LEVEL_VISIBILITY", "3"); // visible and non-visible
                resColl.Add("CUBE_NAME", Context.CurrentCube); // visible and non-visible
                var dsLevels = conn.GetSchemaDataSet("MDSCHEMA_LEVELS", resColl);
                foreach (DataRow dr in dsLevels.Tables[0].Rows)
                {
                    var sColName = string.Format("[${0}.[{1}]", dr["DIMENSION_UNIQUE_NAME"].ToString().Substring(1), dr["LEVEL_NAME"].ToString());
                    if (!translations.ContainsKey(sColName))
                    {
                        translations.Add(sColName, dr["LEVEL_CAPTION"].ToString());
                    }
                }

                // get measure names
                resColl.Clear();
                resColl.Add("CUBE_NAME", Context.CurrentCube);
                resColl.Add("MEASURE_VISIBILITY", 3); // visible and non-visible
                var dsMeasures = conn.GetSchemaDataSet("MDSCHEMA_MEASURES", resColl);
                foreach (DataRow dr in dsMeasures.Tables[0].Rows)
                {
                    if (!translations.ContainsKey(string.Format("[{0}].[{1}]", dr["MEASUREGROUP_NAME"].ToString(), dr["MEASURE_NAME"].ToString())))
                    {
                        translations.Add(string.Format("[{0}].[{1}]", dr["MEASUREGROUP_NAME"].ToString(), dr["MEASURE_NAME"].ToString()), dr["MEASURE_CAPTION"].ToString());
                    }
                }

                // get dimension names
                resColl.Clear();
                resColl.Add("CUBE_NAME", Context.CurrentCube);
                var dsDims = conn.GetSchemaDataSet("MDSCHEMA_DIMENSIONS", resColl);


                AdomdClient.AdomdCommand cmd = new AdomdClient.AdomdCommand();
                cmd.Connection = conn;
                cmd.CommandText = sDrillthroughMDX;
                DataTable tbl = new DataTable();
                AdomdClient.AdomdDataAdapter adp = new AdomdClient.AdomdDataAdapter(cmd);
                TimeoutUtility.FillAdomdDataAdapter(adp, tbl);

                // loop through the columns looking for duplicate translation names
                Dictionary<string, int> dictColumnNames = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);
                foreach (DataColumn col in tbl.Columns)
                {
                    var colKey = col.ColumnName.Substring(0, col.ColumnName.LastIndexOf(']') + 1);

                    if (translations.ContainsKey(colKey))
                    {
                        string sNewColumnName = translations[colKey];
                        if (dictColumnNames.ContainsKey(sNewColumnName))
                            dictColumnNames[sNewColumnName]++;
                        else
                            dictColumnNames.Add(sNewColumnName, 1);
                    }
                    else
                    {
                        Context.TraceEvent(999, 0, string.Format("The translation for the column '{0}' was not found", col.ColumnName));
                    }
                }


                foreach (DataColumn col in tbl.Columns)
                {
                    var colKey = col.ColumnName.Substring(0, col.ColumnName.LastIndexOf(']') + 1);
                    var suffix = col.ColumnName.Substring(col.ColumnName.LastIndexOf("]") + 1);
                    if (translations.ContainsKey(colKey))
                    {
                        string sNewName = translations[colKey];
                        if (dictColumnNames[sNewName] > 1)
                        {

                            //if (string.IsNullOrWhiteSpace( suffix)){
                            //prefix with tablename
                            var m = columnNameRegex.Matches(col.ColumnName);
                            var dimName = m[0].Groups["cube"].Value.TrimStart('$');
                            var dimCaption = dsDims.Tables[0].Select(string.Format("DIMENSION_NAME = '{0}'", dimName))[0]["DIMENSION_CAPTION"].ToString();
                            sNewName = dimCaption + "." + sNewName + suffix;
                            //}
                            //else {
                            //    col.ColumnName = sNewName + suffix;
                            //}
                        }
                        Context.TraceEvent(999, 0, string.Format("translating: '{0}' to '{1}'", col.ColumnName, sNewName));
                        col.ColumnName = sNewName;

                    }
                }




                //foreach (DataColumn col in tbl.Columns)
                //{
                //    string sNewColumnName = col.ColumnName.Substring(col.ColumnName.LastIndexOf('.') + 1).Replace("[", "").Replace("]", "");
                //    if (dictColumnNames[sNewColumnName] > 1)
                //        sNewColumnName = col.ColumnName.Substring(col.ColumnName.LastIndexOf('[') + 1).Replace("[", "").Replace("]", "").Replace("$", "");
                //    if (!tbl.Columns.Contains(sNewColumnName))
                //        col.ColumnName = sNewColumnName;
                //}

                return tbl;
            }
            catch (Exception ex)
            {
                Context.TraceEvent(999, 0, string.Format("Unhandled Exception: {0}", ex.Message));
                return null;
            }
            finally
            {
                conn.Close();
            }
        }

        public static string CurrentCellAttributes()
        {
            return CurrentCellAttributesForCube(AMOHelpers.GetCurrentCubeName());
        }

        public static string CurrentCellAttributesForCube(string CubeName)
        {
            // start with empty string
            StringBuilder coordinate = new StringBuilder();
            bool first = true;
            foreach (Dimension d in Context.Cubes[CubeName].Dimensions)
            {
                foreach (Hierarchy h in d.AttributeHierarchies)
                {
                    // skip user hierarchies - consider attribute and parent-child hierarchies
                    // (parent-child is both user and attribute hierarchy)
                    if (h.HierarchyOrigin == HierarchyOrigin.UserHierarchy)
                        continue;

                    // skip calculated measures
                    if (d.DimensionType == DimensionTypeEnum.Measure && h.CurrentMember.Type == MemberTypeEnum.Formula)
                        continue;

                    if (!first)
                        coordinate.Append("\r\n,");
                    else
                        first = false;

                    coordinate.Append(h.CurrentMember.UniqueName);
                }
            }

            return coordinate.ToString();
        }

        public static string CurrentCellAttributes(Tuple tuple)
        {
            return CurrentCellAttributes(tuple, false);
        }

        public static string CurrentCellAttributes(Tuple tuple, bool skipDefaultMembers)
        {
            if (tuple == null) return FindCurrentMembers.FindCurrentTuple();// CurrentCellAttributes();

            // start with empty string
            StringBuilder coordinate = new StringBuilder();
            bool first = true;
            foreach (Dimension d in Context.Cubes[AMOHelpers.GetCurrentCubeName()].Dimensions)
            {
                foreach (Hierarchy h in d.AttributeHierarchies)
                {
                    // skip user hierarchies - consider attribute and parent-child hierarchies
                    // (parent-child is both user and attribute hierarchy)
                    if (h.HierarchyOrigin == HierarchyOrigin.UserHierarchy)
                        continue;

                    // skip calculated measures
                    string sOverrideMeasure = null;
                    if (d.DimensionType == DimensionTypeEnum.Measure)
                    {
                        foreach (Member m in tuple.Members)
                        {
                            try
                            {
                                if (m.ParentLevel.ParentHierarchy.ParentDimension.DimensionType == DimensionTypeEnum.Measure)
                                {
                                    sOverrideMeasure = m.UniqueName;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                // DPG 16 Jan 2015
                                // if we get an error trying to figure out if we are looking at the Measures Dimension
                                // we can just log it and continue. As far as I'm aware this only happens when one of the dimensions/attributes
                                // in the tuple is not visible and this should only occur for non-measures dimensions.
                                // so simply logging the exception and moving on to the next member should not do any harm.
                                int eventSubClass = 999;
                                Context.TraceEvent(eventSubClass, 0, string.Format("ERROR GetCustomDrillthroughMDX() Exception: {0}", ex.Message));
                            }
                        }

                        if (sOverrideMeasure == null && h.CurrentMember.Type == MemberTypeEnum.Formula)
                            continue;
                    }

                    var sCurrMbr = new Expression(h.UniqueName + ".CurrentMember.UniqueName").Calculate(tuple).ToString();

                    // If skipDefaultMembers is true and the current member is the default member
                    // the move on to the next hierarchy
                    if (sCurrMbr == null || (sCurrMbr == h.DefaultMember && skipDefaultMembers)) continue;

                    if (!first)
                        coordinate.Append("\r\n,");
                    else
                        first = false;

                    if (sOverrideMeasure != null)
                        coordinate.Append(sOverrideMeasure);
                    else //calculate it in the context of the tuple
                        coordinate.Append(sCurrMbr);
                }
            }

            return coordinate.ToString();
        }


        //don't use this in the MDX script or it may return calculated measures from the previous processed version or blow up if the cube was not previously processed
        public static Set GetMeasureGroupCalculatedMeasures(string sMeasureGroupName)
        {
            StringBuilder sb = new StringBuilder();
            XmlaDiscover discover = new XmlaDiscover();
            DataTable table = discover.Discover("MDSCHEMA_MEASURES", "<CUBE_NAME>" + AMOHelpers.GetCurrentCubeName() + "</CUBE_NAME><MEASUREGROUP_NAME>" + sMeasureGroupName + "</MEASUREGROUP_NAME>");
            foreach (DataRow row in table.Rows)
            {
                if (Convert.ToInt32(row["MEASURE_AGGREGATOR"]) == 127)
                {
                    if (sb.Length > 0) sb.Append(",");
                    sb.Append(row["MEASURE_UNIQUE_NAME"].ToString());
                }
            }
            return new Expression("{" + sb.ToString() + "}").CalculateMdxObject(null).ToSet();
        }

    }
}
