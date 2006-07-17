/*============================================================================
  File:    AsymmetricSet.cs

  Summary: Implements a function that generates an Asymmetric set for use in
           MDX Queries.

  Date:    July 12, 2006

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

namespace ASStoredProcs
{

    public class Sets
    {
        #region "Public Interface - allows between 2 and 8 members to be passed in"

        public static Set AsymmetricSet(Member member1, Member member2, Member member3, Member member4, Member member5, Member member6, Member member7, Member member8)
        {
            return buildAsymmetricSet(new Member[] { member1, member2, member3, member4, member5, member6, member7, member8 });
        }

        public static Set AsymmetricSet(Member member1, Member member2, Member member3, Member member4, Member member5, Member member6, Member member7)
        {
            return buildAsymmetricSet(new Member[] { member1, member2, member3, member4, member5, member6, member7 });
        }

        public static Set AsymmetricSet(Member member1, Member member2, Member member3, Member member4,Member member5,Member member6)
        {
            return buildAsymmetricSet(new Member[] { member1, member2, member3, member4,member5,member6 });
        }
        
        public static Set AsymmetricSet(Member member1, Member member2, Member member3, Member member4,Member member5)
        {
            return buildAsymmetricSet(new Member[] { member1, member2, member3, member4,member5 });
        }

        public static Set AsymmetricSet(Member member1, Member member2, Member member3, Member member4)
        {
            return buildAsymmetricSet(new Member[] { member1, member2, member3, member4 });
        }

        public static Set AsymmetricSet(Member member1, Member member2, Member member3)
        {
            return buildAsymmetricSet(new Member[] { member1, member2,member3 });
        }

        public static Set AsymmetricSet(Member member1, Member member2)
        {
            return buildAsymmetricSet(new Member[]{member1,member2});
        }

        public static Set AsymmetricSet(Tuple t)
        {
            Member[] mbrs = new Member[t.Members.Count];
            for (int i =0; i <= t.Members.Count-1 ; i++)
            {
                mbrs[i] = t.Members[i];
            }
            return buildAsymmetricSet(mbrs);

        }
        #endregion

        #region "Implementation"
        private static Set buildAsymmetricSet(params Member[] memberList)
        {
            
            // build a list of all the unique Hierarchies from the members in memberList.
            List<Hierarchy> hierList = new List<Hierarchy>();
            foreach (Member m in memberList)
            {
                // Check that the member variable is correctly populated. If the user passes
                // in a non-existant member we get a member object whose properties are all
                // null or empty strings.
                if (m.UniqueName.Length > 0)
                {
                    if (!hierList.Exists(delegate(Hierarchy h) { if (h.UniqueName == m.ParentLevel.ParentHierarchy.UniqueName) return true; else return false; }))
                        hierList.Add(m.ParentLevel.ParentHierarchy);
                }
            }
            
            // SetBuilder & TupleBuilder are IDisposeable so we ensure they 
            // are disposed by utilizing a using block.
            using (SetBuilder asymSet = new SetBuilder())
            {
                
                foreach (Member paramMbr in memberList)
                {
                    if (paramMbr.UniqueName.Length > 0)
                    {
                        // create a tuple for each member that was passed in,
                        // combined with the default member from the other hierarchies.    
                        using (TupleBuilder tb = new TupleBuilder())
                        {
                            foreach (Hierarchy h in hierList) // for each unique hierarchy
                            {
                                Hierarchy paramHier = paramMbr.ParentLevel.ParentHierarchy;
                                if (paramHier.UniqueName == h.UniqueName)
                                {
                                    //System.Diagnostics.Trace.WriteLine("Adding member " + paramMbr.UniqueName);
                                    tb.Add(paramMbr);
                                }
                                else
                                {
                                    Member defMbr = MDX.StrToSet(h.DefaultMember).Tuples[0].Members[0];
                                    //System.Diagnostics.Trace.WriteLine("Adding default member " + defMbr.UniqueName);                         
                                    tb.Add(defMbr);
                                }
                            }
                            Tuple t = tb.ToTuple();

                            // if the members added to the TupleBuilder will result in a non-existant tuple
                            // (eg. [Calendar Quarter 1] and [December])  the ToTuple method returns a Tuple
                            // containing 0 members. If such a tuple is added to the SetBuilder, the
                            // SetBuilder.ToSet will throw an exception
                            if (t.Members.Count > 0) { asymSet.Add(tb.ToTuple()); }
                        }// using tb
                    }
                } //foreach paramMbr        
                return asymSet.ToSet();
            } //using SetBuilder
        }
        #endregion
    }
}
