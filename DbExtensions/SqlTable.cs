﻿// Copyright 2012-2014 Max Toro Q.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Data.Linq.Mapping;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DbExtensions {

   /// <summary>
   /// A non-generic version of <see cref="SqlTable&lt;TEntity>"/> which can be used when the type of the entity is not known at build time.
   /// This class cannot be instantiated.
   /// </summary>
   /// <seealso cref="Database.Table(Type)"/>
   [DebuggerDisplay("{metaType.Name}")]
   public sealed class SqlTable : SqlSet, ISqlTable {

      // table is the SqlTable<TEntity> instance for metaType
      // SqlTable is only a wrapper on SqlTable<TEntity>

      readonly ISqlTable table;

      readonly MetaType metaType;
      readonly SqlCommandBuilder<object> sqlCommands;

      /// <summary>
      /// Gets a <see cref="SqlCommandBuilder&lt;Object>"/> object for the current table.
      /// </summary>
      public SqlCommandBuilder<object> SQL {
         get { return sqlCommands; }
      }

      internal static SqlBuilder SELECT_(MetaType metaType, IEnumerable<MetaDataMember> selectMembers, string tableAlias, Database db) {

         if (selectMembers == null)
            selectMembers = metaType.PersistentDataMembers.Where(m => !m.IsAssociation);

         SqlBuilder query = new SqlBuilder();

         string qualifier = (!String.IsNullOrEmpty(tableAlias)) ?
            db.QuoteIdentifier(tableAlias) + "." : null;

         IEnumerator<MetaDataMember> enumerator = selectMembers.GetEnumerator();

         while (enumerator.MoveNext()) {

            string mappedName = enumerator.Current.MappedName;
            string memberName = enumerator.Current.Name;
            string columnAlias = !String.Equals(mappedName, memberName, StringComparison.Ordinal) ?
               memberName : null;

            query.SELECT((qualifier ?? "") + db.QuoteIdentifier(enumerator.Current.MappedName));

            if (columnAlias != null)
               query.Buffer.Append(" AS ").Append(db.QuoteIdentifier(memberName));
         }

         return query;
      }

      internal static SqlBuilder SELECT_FROM(MetaType metaType, IEnumerable<MetaDataMember> selectMembers, string tableAlias, Database db) {

         if (metaType.Table == null) throw new InvalidOperationException("metaType.Table cannot be null.");

         SqlBuilder query = SELECT_(metaType, selectMembers, tableAlias, db);

         string alias = (!String.IsNullOrEmpty(tableAlias)) ?
            " " + db.QuoteIdentifier(tableAlias) : null;

         return query.FROM(db.QuoteIdentifier(metaType.Table.TableName) + (alias ?? ""));
      }

      internal static void EnsureEntityType(MetaType metaType) {

         if (!metaType.IsEntity) {
            throw new InvalidOperationException(
               String.Format(CultureInfo.InvariantCulture,
                  "The operation is not available for non-entity types ('{0}').", metaType.Type.FullName)
            );
         }
      }

      internal SqlTable(Database db, MetaType metaType, ISqlTable table)
         : base(SELECT_FROM(metaType, null, null, db), metaType.Type, db, adoptQuery: true) {

         this.table = table;

         this.metaType = metaType;
         this.sqlCommands = new SqlCommandBuilder<object>(db, metaType);
      }

      /// <summary>
      /// Casts the current <see cref="SqlTable"/> to the generic <see cref="SqlTable&lt;TEntity>"/> instance.
      /// </summary>
      /// <typeparam name="TEntity">The type of the entity.</typeparam>
      /// <returns>The <see cref="SqlTable&lt;TEntity>"/> instance for <typeparamref name="TEntity"/>.</returns>
      /// <exception cref="System.InvalidOperationException">The specified <typeparamref name="TEntity"/> is not valid for this instance.</exception>
      public new SqlTable<TEntity> Cast<TEntity>() where TEntity : class {

         if (typeof(TEntity) != this.metaType.Type) 
            throw new InvalidOperationException("The specified type parameter is not valid for this instance.");

         return (SqlTable<TEntity>)table;
      }

      /// <summary>
      /// Casts the elements of the set to the specified type.
      /// </summary>
      /// <param name="resultType">The type to cast the elements of the set to.</param>
      /// <returns>A new <see cref="SqlSet"/> that contains each element of the current set cast to the specified type.</returns>
      [EditorBrowsable(EditorBrowsableState.Never)]
      public new SqlSet Cast(Type resultType) {
         return base.Cast(resultType);
      }

      #region ISqlTable Members

      // These methods just call the same method on this.table

      /// <summary>
      /// Gets the entity whose primary key matches the <paramref name="id"/> parameter.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      /// <returns>
      /// The entity whose primary key matches the <paramref name="id"/> parameter, 
      /// or null if the <paramref name="id"/> does not exist.
      /// </returns>
      public object Find(object id) {
         return table.Find(id);
      }

      /// <summary>
      /// Executes an INSERT command for the specified <paramref name="entity"/>.
      /// </summary>
      /// <param name="entity">
      /// The object whose INSERT command is to be executed. This parameter is named entity for consistency
      /// with the other CRUD methods, but in this case it doesn't need to be an actual entity, which means it doesn't
      /// need to have a primary key.
      /// </param>
      public void Insert(object entity) {
         table.Insert(entity);
      }

      /// <summary>
      /// Executes an INSERT command for the specified <paramref name="entity"/>.
      /// </summary>
      /// <param name="entity">
      /// The object whose INSERT command is to be executed. This parameter is named entity for consistency
      /// with the other CRUD methods, but in this case it doesn't need to be an actual entity, which means it doesn't
      /// need to have a primary key.
      /// </param>
      /// <param name="deep">true to recursively execute INSERT commands for the <paramref name="entity"/>'s one-to-many associations; otherwise, false.</param>
      public void Insert(object entity, bool deep) {
         table.Insert(entity, deep);
      }

      /// <summary>
      /// Recursively executes INSERT commands for the specified <paramref name="entity"/> and all its
      /// one-to-many associations.
      /// </summary>
      /// <param name="entity">The entity whose INSERT command is to be executed.</param>
      [Obsolete("Please use Insert(TEntity, Boolean) instead.")]
      [EditorBrowsable(EditorBrowsableState.Never)]
      public void InsertDeep(object entity) {
         table.InsertDeep(entity);
      }

      void ISqlTable.InsertDescendants(object entity) {
         table.InsertDescendants(entity);
      }

      /// <summary>
      /// Executes INSERT commands for the specified <paramref name="entities"/>.
      /// </summary>
      /// <param name="entities">The entities whose INSERT commands are to be executed.</param>
      public void InsertRange(IEnumerable<object> entities) {
         table.InsertRange(entities);
      }

      /// <summary>
      /// Executes INSERT commands for the specified <paramref name="entities"/>.
      /// </summary>
      /// <param name="entities">The entities whose INSERT commands are to be executed.</param>
      /// <param name="deep">true to recursively execute INSERT commands for each entity's one-to-many associations; otherwise, false.</param>
      public void InsertRange(IEnumerable<object> entities, bool deep) {
         table.InsertRange(entities, deep);
      }

      /// <summary>
      /// Executes INSERT commands for the specified <paramref name="entities"/>.
      /// </summary>
      /// <param name="entities">The entities whose INSERT commands are to be executed.</param>
      public void InsertRange(params object[] entities) {
         table.InsertRange(entities);
      }

      /// <summary>
      /// Executes INSERT commands for the specified <paramref name="entities"/>.
      /// </summary>
      /// <param name="entities">The entities whose INSERT commands are to be executed.</param>
      /// <param name="deep">true to recursively execute INSERT commands for each entity's one-to-many associations; otherwise, false.</param>
      public void InsertRange(object[] entities, bool deep) {
         table.InsertRange(entities, deep);
      }

      /// <summary>
      /// Executes an UPDATE command for the specified <paramref name="entity"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose UPDATE command is to be executed.</param>
      public void Update(object entity) {
         table.Update(entity);
      }

      /// <summary>
      /// Executes an UPDATE command for the specified <paramref name="entity"/>
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose UPDATE command is to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the UPDATE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void Update(object entity, ConcurrencyConflictPolicy conflictPolicy) {
         table.Update(entity, conflictPolicy);
      }

      /// <summary>
      /// Executes UPDATE commands for the specified <paramref name="entities"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose UPDATE commands are to be executed.</param>
      public void UpdateRange(IEnumerable<object> entities) {
         table.UpdateRange(entities);
      }

      /// <summary>
      /// Executes UPDATE commands for the specified <paramref name="entities"/>
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose UPDATE commands are to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the UPDATE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void UpdateRange(IEnumerable<object> entities, ConcurrencyConflictPolicy conflictPolicy) {
         table.UpdateRange(entities, conflictPolicy);
      }

      /// <summary>
      /// Executes UPDATE commands for the specified <paramref name="entities"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose UPDATE commands are to be executed.</param>
      public void UpdateRange(params object[] entities) {
         table.UpdateRange(entities);
      }

      /// <summary>
      /// Executes UPDATE commands for the specified <paramref name="entities"/>
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose UPDATE commands are to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the UPDATE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void UpdateRange(object[] entities, ConcurrencyConflictPolicy conflictPolicy) {
         table.UpdateRange(entities, conflictPolicy);
      }

      /// <summary>
      /// Executes a DELETE command for the specified <paramref name="entity"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose DELETE command is to be executed.</param>
      public void Delete(object entity) {
         table.Delete(entity);
      }

      /// <summary>
      /// Executes a DELETE command for the specified <paramref name="entity"/>
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose DELETE command is to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the DELETE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void Delete(object entity, ConcurrencyConflictPolicy conflictPolicy) {
         table.Delete(entity, conflictPolicy);
      }

      /// <summary>
      /// Executes a DELETE command for the entity
      /// whose primary key matches the <paramref name="id"/> parameter,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      [Obsolete("Please use DeleteKey(Object) instead.")]
      [EditorBrowsable(EditorBrowsableState.Never)]
      public void DeleteById(object id) {
         table.DeleteById(id);
      }

      /// <summary>
      /// Executes a DELETE command for the entity
      /// whose primary key matches the <paramref name="id"/> parameter,
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies how to validate the affected records value.
      /// </param>
      [Obsolete("Please use DeleteKey(Object, ConcurrencyConflictPolicy) instead.")]
      [EditorBrowsable(EditorBrowsableState.Never)]
      public void DeleteById(object id, ConcurrencyConflictPolicy conflictPolicy) {
         table.DeleteById(id, conflictPolicy);
      }

      /// <summary>
      /// Executes a DELETE command for the entity
      /// whose primary key matches the <paramref name="id"/> parameter,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      public void DeleteKey(object id) {
         table.DeleteKey(id);
      }

      /// <summary>
      /// Executes a DELETE command for the entity
      /// whose primary key matches the <paramref name="id"/> parameter,
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies how to validate the affected records value.
      /// </param>
      public void DeleteKey(object id, ConcurrencyConflictPolicy conflictPolicy) {
         table.DeleteKey(id, conflictPolicy);
      }

      /// <summary>
      /// Executes DELETE commands for the specified <paramref name="entities"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose DELETE commands are to be executed.</param>
      public void DeleteRange(IEnumerable<object> entities) {
         table.DeleteRange(entities);
      }

      /// <summary>
      /// Executes DELETE commands for the specified <paramref name="entities"/>,
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose DELETE commands are to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the DELETE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void DeleteRange(IEnumerable<object> entities, ConcurrencyConflictPolicy conflictPolicy) {
         table.DeleteRange(entities, conflictPolicy);
      }

      /// <summary>
      /// Executes DELETE commands for the specified <paramref name="entities"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose DELETE commands are to be executed.</param>
      public void DeleteRange(params object[] entities) {
         table.DeleteRange(entities);
      }

      /// <summary>
      /// Executes DELETE commands for the specified <paramref name="entities"/>,
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose DELETE commands are to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the DELETE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void DeleteRange(object[] entities, ConcurrencyConflictPolicy conflictPolicy) {
         table.DeleteRange(entities, conflictPolicy);
      }

      /// <summary>
      /// Checks the existance of the <paramref name="entity"/>,
      /// using the primary key value. Version members are ignored.
      /// </summary>
      /// <param name="entity">The entity whose existance is to be checked.</param>
      /// <returns>true if the primary key value exists in the database; otherwise false.</returns>
      public bool Contains(object entity) {
         return table.Contains(entity);
      }

      /// <summary>
      /// Checks the existance of the <paramref name="entity"/>,
      /// using the primary key and optionally version column.
      /// </summary>
      /// <param name="entity">The entity whose existance is to be checked.</param>
      /// <param name="version">true to check the version column; otherwise, false.</param>
      /// <returns>true if the primary key and version combination exists in the database; otherwise, false.</returns>
      public bool Contains(object entity, bool version) {
         return table.Contains(entity, version);
      }

      /// <summary>
      /// Checks the existance of an entity whose primary matches the <paramref name="id"/> parameter.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      /// <returns>true if the primary key value exists in the database; otherwise false.</returns>
      public bool ContainsKey(object id) {
         return table.ContainsKey(id);
      }

      /// <summary>
      /// Sets all mapped members of <paramref name="entity"/> to their default database values.
      /// </summary>
      /// <param name="entity">The entity whose members are to be set to their default values.</param>
      /// <seealso cref="DbConnection.GetSchema(string, string[])"/>
      [EditorBrowsable(EditorBrowsableState.Never)]
      [Obsolete("This method will be removed in the next major version.")]
      public void Initialize(object entity) {
         table.Initialize(entity);
      }

      /// <summary>
      /// Sets all mapped members of <paramref name="entity"/> to their most current persisted value.
      /// </summary>
      /// <param name="entity">The entity to refresh.</param>
      public void Refresh(object entity) {
         table.Refresh(entity);
      }

      #endregion
   }

   /// <summary>
   /// A <see cref="SqlSet&lt;TEntity>"/> that provides additional methods for CRUD (Create, Read, Update, Delete)
   /// operations for <typeparamref name="TEntity"/>, mapped using the <see cref="N:System.Data.Linq.Mapping"/> API. 
   /// This class cannot be instantiated.
   /// </summary>
   /// <typeparam name="TEntity">The type of the entity.</typeparam>
   /// <seealso cref="Database.Table&lt;TEntity>()"/>
   [DebuggerDisplay("{metaType.Name}")]
   public sealed class SqlTable<TEntity> : SqlSet<TEntity>, ISqlTable
      where TEntity : class {

      readonly Database db;
      readonly MetaType metaType;
      readonly SqlCommandBuilder<TEntity> sqlCommands;

      /// <summary>
      /// Gets a <see cref="SqlCommandBuilder&lt;TEntity>"/> object for the current table.
      /// </summary>
      public SqlCommandBuilder<TEntity> SQL {
         get { return sqlCommands; }
      }

      internal SqlTable(Database db, MetaType metaType)
         : base(SqlTable.SELECT_FROM(metaType, null, null, db), db, adoptQuery: true) {

         this.db = db;
         this.metaType = metaType;
         this.sqlCommands = new SqlCommandBuilder<TEntity>(db, metaType);
      }

      string QuoteIdentifier(string unquotedIdentifier) {
         return this.db.QuoteIdentifier(unquotedIdentifier);
      }

      string BuildPredicateFragment(IDictionary<string, object> predicateValues, ICollection<object> parametersBuffer) {
         return this.SQL.BuildPredicateFragment(predicateValues, parametersBuffer);
      }

      void EnsureEntityType() {
         SqlTable.EnsureEntityType(metaType);
      }

      // CRUD

      /// <summary>
      /// Gets the entity whose primary key matches the <paramref name="id"/> parameter.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      /// <returns>
      /// The entity whose primary key matches the <paramref name="id"/> parameter, 
      /// or null if the <paramref name="id"/> does not exist.
      /// </returns>
      public TEntity Find(object id) {

         if (id == null) throw new ArgumentNullException("id");

         if (metaType.IdentityMembers.Count == 0) {
            throw new InvalidOperationException("The entity has no identity members defined.");

         } else if (metaType.IdentityMembers.Count > 1) {
            throw new InvalidOperationException("Cannot call this method when the entity has more than one identity member.");
         }

         var predicateValues = new Dictionary<string, object> { 
            { metaType.IdentityMembers[0].MappedName, id }
         };

         SqlBuilder query = this.SQL.SELECT_FROM();
         query.WHERE(BuildPredicateFragment(predicateValues, query.ParameterValues));

         TEntity entity = this.db.Map<TEntity>(query).SingleOrDefault();

         return entity;
      }

      /// <summary>
      /// Executes an INSERT command for the specified <paramref name="entity"/>.
      /// </summary>
      /// <param name="entity">
      /// The object whose INSERT command is to be executed. This parameter is named entity for consistency
      /// with the other CRUD methods, but in this case it doesn't need to be an actual entity, which means it doesn't
      /// need to have a primary key.
      /// </param>
      public void Insert(TEntity entity) {
         Insert(entity, deep: false);
      }

      /// <summary>
      /// Executes an INSERT command for the specified <paramref name="entity"/>.
      /// </summary>
      /// <param name="entity">
      /// The object whose INSERT command is to be executed. This parameter is named entity for consistency
      /// with the other CRUD methods, but in this case it doesn't need to be an actual entity, which means it doesn't
      /// need to have a primary key.
      /// </param>
      /// <param name="deep">true to recursively execute INSERT commands for the <paramref name="entity"/>'s one-to-many associations; otherwise, false.</param>
      public void Insert(TEntity entity, bool deep) {

         if (entity == null) throw new ArgumentNullException("entity");

         SqlBuilder insertSql = this.SQL.INSERT_INTO_VALUES(entity);

         MetaDataMember idMember = metaType.DBGeneratedIdentityMember;

         MetaDataMember[] syncMembers =
            (from m in metaType.PersistentDataMembers
             where (m.AutoSync == AutoSync.Always || m.AutoSync == AutoSync.OnInsert)
               && m != idMember
             select m).ToArray();

         using (var tx = this.db.EnsureInTransaction()) {

            // Transaction is required by SQLCE 4.0
            // https://connect.microsoft.com/SQLServer/feedback/details/653675/sql-ce-4-0-select-identity-returns-null

            this.db.AffectOne(insertSql);

            if (idMember != null) {

               object id = this.db.LastInsertId();

               if (Convert.IsDBNull(id) || id == null)
                  throw new DataException("The last insert id value cannot be null.");

               object convertedId;

               try {
                  convertedId = Convert.ChangeType(id, idMember.Type, CultureInfo.InvariantCulture);

               } catch (InvalidCastException ex) {
                  throw new DataException("Couldn't convert the last insert id value to the appropiate type (see inner exception for details).", ex);
               }

               object entityObj = (object)entity;

               idMember.MemberAccessor.SetBoxedValue(ref entityObj, convertedId);
            }

            if (syncMembers.Length > 0
               && metaType.IsEntity) {
               
               Refresh(entity, syncMembers);
            }

            if (deep)
               InsertDescendants(entity);

            tx.Commit();
         }
      }

      /// <summary>
      /// Recursively executes INSERT commands for the specified <paramref name="entity"/> and all its
      /// one-to-many associations.
      /// </summary>
      /// <param name="entity">The entity whose INSERT command is to be executed.</param>
      [Obsolete("Please use Insert(TEntity, Boolean) instead.")]
      [EditorBrowsable(EditorBrowsableState.Never)]
      public void InsertDeep(TEntity entity) {
         Insert(entity, deep: true);
      }

      void InsertDescendants(TEntity entity) {

         MetaAssociation[] oneToMany = metaType.Associations.Where(a => a.IsMany).ToArray();

         for (int i = 0; i < oneToMany.Length; i++) {

            MetaAssociation assoc = oneToMany[i];

            object[] many = ((IEnumerable<object>)assoc.ThisMember.MemberAccessor.GetBoxedValue(entity) ?? new object[0])
               .Where(o => o != null)
               .ToArray();

            if (many.Length == 0) continue;

            for (int j = 0; j < many.Length; j++) {

               object child = many[j];

               for (int k = 0; k < assoc.ThisKey.Count; k++) {

                  MetaDataMember thisKey = assoc.ThisKey[k];
                  MetaDataMember otherKey = assoc.OtherKey[k];

                  object thisKeyVal = thisKey.MemberAccessor.GetBoxedValue(entity);

                  otherKey.MemberAccessor.SetBoxedValue(ref child, thisKeyVal);
               }
            }

            SqlTable otherTable = this.db.Table(assoc.OtherType);

            otherTable.InsertRange(many);

            for (int j = 0; j < many.Length; j++) {

               object child = many[j];

               ((ISqlTable)otherTable).InsertDescendants(child);
            }
         }
      }

      /// <summary>
      /// Executes INSERT commands for the specified <paramref name="entities"/>.
      /// </summary>
      /// <param name="entities">The entities whose INSERT commands are to be executed.</param>
      public void InsertRange(IEnumerable<TEntity> entities) {

         if (entities == null) throw new ArgumentNullException("entities");

         InsertRange(entities.ToArray());
      }

      /// <summary>
      /// Executes INSERT commands for the specified <paramref name="entities"/>.
      /// </summary>
      /// <param name="entities">The entities whose INSERT commands are to be executed.</param>
      /// <param name="deep">true to recursively execute INSERT commands for each entity's one-to-many associations; otherwise, false.</param>
      public void InsertRange(IEnumerable<TEntity> entities, bool deep) {

         if (entities == null) throw new ArgumentNullException("entities");

         InsertRange(entities.ToArray(), deep);
      }

      /// <summary>
      /// Executes INSERT commands for the specified <paramref name="entities"/>.
      /// </summary>
      /// <param name="entities">The entities whose INSERT commands are to be executed.</param>
      public void InsertRange(params TEntity[] entities) {
         InsertRange(entities, deep: false);
      }

      /// <summary>
      /// Executes INSERT commands for the specified <paramref name="entities"/>.
      /// </summary>
      /// <param name="entities">The entities whose INSERT commands are to be executed.</param>
      /// <param name="deep">true to recursively execute INSERT commands for each entity's one-to-many associations; otherwise, false.</param>
      public void InsertRange(TEntity[] entities, bool deep) {

         if (entities == null) throw new ArgumentNullException("entities");

         entities = entities.Where(o => o != null).ToArray();

         if (entities.Length == 0)
            return;

         if (entities.Length == 1) {
            Insert(entities[0], deep);
            return;
         }

         MetaDataMember[] syncMembers =
            (from m in metaType.PersistentDataMembers
             where (m.AutoSync == AutoSync.Always || m.AutoSync == AutoSync.OnInsert)
             select m).ToArray();

         bool batch = syncMembers.Length == 0 
            && this.db.Configuration.EnableBatchCommands;

         if (batch) {

            SqlBuilder batchInsert = SqlBuilder.JoinSql(";" + Environment.NewLine, entities.Select(e => this.SQL.INSERT_INTO_VALUES(e)));

            using (var tx = this.db.EnsureInTransaction()) {
               
               this.db.Affect(batchInsert, entities.Length, AffectedRecordsPolicy.MustMatchAffecting);

               if (deep) {
                  for (int i = 0; i < entities.Length; i++)
                     InsertDescendants(entities[i]);
               }

               tx.Commit();
            }

         } else {

            using (var tx = this.db.EnsureInTransaction()) {

               for (int i = 0; i < entities.Length; i++)
                  Insert(entities[i], deep);

               tx.Commit();
            }
         }
      }

      /// <summary>
      /// Executes an UPDATE command for the specified <paramref name="entity"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose UPDATE command is to be executed.</param>
      public void Update(TEntity entity) {
         Update(entity, this.db.Configuration.UpdateConflictPolicy);
      }

      /// <summary>
      /// Executes an UPDATE command for the specified <paramref name="entity"/>
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose UPDATE command is to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the UPDATE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void Update(TEntity entity, ConcurrencyConflictPolicy conflictPolicy) {

         if (entity == null) throw new ArgumentNullException("entity");

         SqlBuilder updateSql = this.SQL.UPDATE_SET_WHERE(entity, conflictPolicy);

         AffectedRecordsPolicy affRec = GetAffectedRecordsPolicy(conflictPolicy);

         MetaDataMember[] syncMembers =
            (from m in metaType.PersistentDataMembers
             where m.AutoSync == AutoSync.Always || m.AutoSync == AutoSync.OnUpdate
             select m).ToArray();

         using (this.db.EnsureConnectionOpen()) {

            this.db.Affect(updateSql, 1, affRec);

            if (syncMembers.Length > 0)
               Refresh(entity, syncMembers);
         }
      }

      /// <summary>
      /// Executes UPDATE commands for the specified <paramref name="entities"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose UPDATE commands are to be executed.</param>
      public void UpdateRange(IEnumerable<TEntity> entities) {
         
         if (entities == null) throw new ArgumentNullException("entities");

         UpdateRange(entities.ToArray());
      }

      /// <summary>
      /// Executes UPDATE commands for the specified <paramref name="entities"/>
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose UPDATE commands are to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the UPDATE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void UpdateRange(IEnumerable<TEntity> entities, ConcurrencyConflictPolicy conflictPolicy) {

         if (entities == null) throw new ArgumentNullException("entities");

         UpdateRange(entities.ToArray(), conflictPolicy);
      }

      /// <summary>
      /// Executes UPDATE commands for the specified <paramref name="entities"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose UPDATE commands are to be executed.</param>
      public void UpdateRange(params TEntity[] entities) {
         UpdateRange(entities, this.db.Configuration.UpdateConflictPolicy);
      }

      /// <summary>
      /// Executes UPDATE commands for the specified <paramref name="entities"/>
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose UPDATE commands are to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the UPDATE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void UpdateRange(TEntity[] entities, ConcurrencyConflictPolicy conflictPolicy) {

         if (entities == null) throw new ArgumentNullException("entities");

         entities = entities.Where(o => o != null).ToArray();

         if (entities.Length == 0)
            return;

         if (entities.Length == 1) {
            Update(entities[0], conflictPolicy);
            return;
         }

         EnsureEntityType();

         MetaDataMember[] syncMembers =
            (from m in metaType.PersistentDataMembers
             where m.AutoSync == AutoSync.Always || m.AutoSync == AutoSync.OnUpdate
             select m).ToArray();

         bool batch = syncMembers.Length == 0
            && this.db.Configuration.EnableBatchCommands;

         if (batch) {

            SqlBuilder batchUpdate = SqlBuilder.JoinSql(";" + Environment.NewLine, entities.Select(e => this.SQL.UPDATE_SET_WHERE(e, conflictPolicy)));

            AffectedRecordsPolicy affRec = GetAffectedRecordsPolicy(conflictPolicy);

            this.db.Affect(batchUpdate, entities.Length, affRec);

         } else {

            using (var tx = this.db.EnsureInTransaction()) {

               for (int i = 0; i < entities.Length; i++)
                  Update(entities[i], conflictPolicy);

               tx.Commit();
            }
         }
      }

      /// <summary>
      /// Executes a DELETE command for the specified <paramref name="entity"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose DELETE command is to be executed.</param>
      public void Delete(TEntity entity) {
         Delete(entity, this.db.Configuration.DeleteConflictPolicy);
      }

      /// <summary>
      /// Executes a DELETE command for the specified <paramref name="entity"/>
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose DELETE command is to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the DELETE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void Delete(TEntity entity, ConcurrencyConflictPolicy conflictPolicy) {

         if (entity == null) throw new ArgumentNullException("entity");

         AffectedRecordsPolicy affRec = GetAffectedRecordsPolicy(conflictPolicy);

         this.db.Affect(this.SQL.DELETE_FROM_WHERE(entity, conflictPolicy), 1, affRec);
      }

      /// <summary>
      /// Executes a DELETE command for the entity
      /// whose primary key matches the <paramref name="id"/> parameter,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      [Obsolete("Please use DeleteKey(Object) instead.")]
      [EditorBrowsable(EditorBrowsableState.Never)]
      public void DeleteById(object id) {
         DeleteKey(id);
      }

      /// <summary>
      /// Executes a DELETE command for the entity
      /// whose primary key matches the <paramref name="id"/> parameter,
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies how to validate the affected records value.
      /// </param>
      [Obsolete("Please use DeleteKey(Object, ConcurrencyConflictPolicy) instead.")]
      [EditorBrowsable(EditorBrowsableState.Never)]
      public void DeleteById(object id, ConcurrencyConflictPolicy conflictPolicy) {
         DeleteKey(id, conflictPolicy);
      }

      /// <summary>
      /// Executes a DELETE command for the entity
      /// whose primary key matches the <paramref name="id"/> parameter,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      public void DeleteKey(object id) {
         DeleteKey(id, this.db.Configuration.DeleteConflictPolicy);
      }

      /// <summary>
      /// Executes a DELETE command for the entity
      /// whose primary key matches the <paramref name="id"/> parameter,
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies how to validate the affected records value.
      /// </param>
      public void DeleteKey(object id, ConcurrencyConflictPolicy conflictPolicy) {
         this.db.Affect(this.SQL.DELETE_FROM_WHERE_id(id), 1, GetAffectedRecordsPolicy(conflictPolicy));
      }

      /// <summary>
      /// Executes DELETE commands for the specified <paramref name="entities"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose DELETE commands are to be executed.</param>
      public void DeleteRange(IEnumerable<TEntity> entities) {

         if (entities == null) throw new ArgumentNullException("entities");

         DeleteRange(entities.ToArray());
      }

      /// <summary>
      /// Executes DELETE commands for the specified <paramref name="entities"/>,
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose DELETE commands are to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the DELETE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void DeleteRange(IEnumerable<TEntity> entities, ConcurrencyConflictPolicy conflictPolicy) {

         if (entities == null) throw new ArgumentNullException("entities");

         DeleteRange(entities.ToArray(), conflictPolicy);
      }

      /// <summary>
      /// Executes DELETE commands for the specified <paramref name="entities"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose DELETE commands are to be executed.</param>
      public void DeleteRange(params TEntity[] entities) {
         DeleteRange(entities, this.db.Configuration.DeleteConflictPolicy);
      }

      /// <summary>
      /// Executes DELETE commands for the specified <paramref name="entities"/>,
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entities">The entities whose DELETE commands are to be executed.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to check for in the DELETE
      /// predicate, and how to validate the affected records value.
      /// </param>
      public void DeleteRange(TEntity[] entities, ConcurrencyConflictPolicy conflictPolicy) {

         if (entities == null) throw new ArgumentNullException("entities");

         entities = entities.Where(o => o != null).ToArray();

         if (entities.Length == 0)
            return;

         if (entities.Length == 1) {
            Delete(entities[0], conflictPolicy);
            return;
         }

         EnsureEntityType();

         AffectedRecordsPolicy affRec = GetAffectedRecordsPolicy(conflictPolicy);

         bool useVersion = conflictPolicy == ConcurrencyConflictPolicy.UseVersion
            && this.metaType.VersionMember != null;

         bool singleStatement = this.metaType.IdentityMembers.Count == 1
            && !useVersion;

         bool batch = this.db.Configuration.EnableBatchCommands;

         if (singleStatement) {

            MetaDataMember idMember = this.metaType.IdentityMembers[0];

            object[] ids = entities.Select(e => idMember.MemberAccessor.GetBoxedValue(e)).ToArray();

            SqlBuilder sql = this.SQL
               .DELETE_FROM()
               .WHERE(this.db.QuoteIdentifier(idMember.MappedName) + " IN ({0})", new object[1] { ids });

            this.db.Affect(sql, entities.Length, affRec);

         } else if (batch) {

            SqlBuilder batchDelete = SqlBuilder.JoinSql(";" + Environment.NewLine, entities.Select(e => this.SQL.DELETE_FROM_WHERE(e, conflictPolicy)));

            this.db.Affect(batchDelete, entities.Length, affRec);

         } else {

            using (var tx = this.db.EnsureInTransaction()) {

               for (int i = 0; i < entities.Length; i++)
                  Delete(entities[i], conflictPolicy);

               tx.Commit();
            }
         }
      }

      static AffectedRecordsPolicy GetAffectedRecordsPolicy(ConcurrencyConflictPolicy conflictPolicy) {

         switch (conflictPolicy) {
            case ConcurrencyConflictPolicy.UseVersion:
            case ConcurrencyConflictPolicy.IgnoreVersion:
               return AffectedRecordsPolicy.MustMatchAffecting;

            case ConcurrencyConflictPolicy.IgnoreVersionAndLowerAffectedRecords:
               return AffectedRecordsPolicy.AllowLower;

            default:
               throw new ArgumentOutOfRangeException("conflictPolicy");
         }
      }

      // Misc

      /// <summary>
      /// Checks the existance of the <paramref name="entity"/>,
      /// using the primary key value. Version members are ignored.
      /// </summary>
      /// <param name="entity">The entity whose existance is to be checked.</param>
      /// <returns>true if the primary key value exists in the database; otherwise false.</returns>
      public bool Contains(TEntity entity) {
         return Contains(entity, version: false);
      }

      /// <summary>
      /// Checks the existance of the <paramref name="entity"/>,
      /// using the primary key and optionally version column.
      /// </summary>
      /// <param name="entity">The entity whose existance is to be checked.</param>
      /// <param name="version">true to check the version column; otherwise, false.</param>
      /// <returns>true if the primary key and version combination exists in the database; otherwise, false.</returns>
      public bool Contains(TEntity entity, bool version) {

         if (entity == null) throw new ArgumentNullException("entity");

         EnsureEntityType();

         MetaDataMember[] predicateMembers =
            (from m in metaType.PersistentDataMembers
             where m.IsPrimaryKey || (m.IsVersion && version)
             select m).ToArray();

         IDictionary<string, object> predicateValues = predicateMembers.ToDictionary(
            m => m.MappedName,
            m => m.MemberAccessor.GetBoxedValue(entity)
         );

         return Contains(predicateMembers, predicateValues);
      }

      /// <summary>
      /// Checks the existance of an entity whose primary matches the <paramref name="id"/> parameter.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      /// <returns>true if the primary key value exists in the database; otherwise false.</returns>
      public bool ContainsKey(object id) {
         return ContainsKey(new object[1] { id });
      }

      bool ContainsKey(object[] keyValues) {

         if (keyValues == null) throw new ArgumentNullException("keyValues");

         EnsureEntityType();

         MetaDataMember[] predicateMembers = metaType.IdentityMembers.ToArray();

         if (keyValues.Length != predicateMembers.Length)
            throw new ArgumentException("The Length of keyValues must match the number of identity members.", "keyValues");

         IDictionary<string, object> predicateValues =
            Enumerable.Range(0, predicateMembers.Length)
               .ToDictionary(i => predicateMembers[i].MappedName, i => keyValues[i]);

         return Contains(predicateMembers, predicateValues);
      }

      bool Contains(MetaDataMember[] predicateMembers, IDictionary<string, object> predicateValues) {

         SqlBuilder query = this.SQL.SELECT_FROM(new[] { predicateMembers[0] });
         query.WHERE(BuildPredicateFragment(predicateValues, query.ParameterValues));

         return this.db.Exists(query);
      }

      /// <summary>
      /// Sets all mapped members of <paramref name="entity"/> to their default database values.
      /// </summary>
      /// <param name="entity">The entity whose members are to be set to their default values.</param>
      /// <seealso cref="DbConnection.GetSchema(string, string[])"/>
      [EditorBrowsable(EditorBrowsableState.Never)]
      [Obsolete("This method will be removed in the next major version.")]
      public void Initialize(TEntity entity) {

         if (entity == null) throw new ArgumentNullException("entity");

         DbConnection conn = this.db.Connection;
         string tableName = metaType.Table.TableName;
         const string collectionName = "Columns";

         using (this.db.EnsureConnectionOpen()) {

            DataTable schema = conn.GetSchema(collectionName, new string[4] { conn.Database, null, tableName, null });

            if (schema.Rows.Count == 0)
               // MySQL Connector/NET
               schema = conn.GetSchema(collectionName, new string[4] { null, conn.Database, tableName, null });

            if (schema.Rows.Count == 0)
               // SQL Server Compact
               schema = conn.GetSchema(collectionName, new string[4] { null, null, tableName, null });

            if (schema.Rows.Count > 0) {

               SqlBuilder query = new SqlBuilder();

               foreach (DataRow row in schema.Rows) {

                  string defaultExpr = (row["COLUMN_DEFAULT"] ?? "").ToString();

                  if (!String.IsNullOrEmpty(defaultExpr)) {

                     string columnName = (row["COLUMN_NAME"] ?? "").ToString();
                     MetaDataMember member = (!String.IsNullOrEmpty(columnName)) ?
                        metaType.PersistentDataMembers.Where(m => !m.IsAssociation && m.MappedName == columnName).SingleOrDefault() :
                        null;

                     if (member != null)
                        query.SELECT(String.Concat(defaultExpr, " AS ", QuoteIdentifier(member.Name)));
                  }
               }

               if (!query.IsEmpty) {

                  PocoMapper mapper = CreatePocoMapper();

                  object entityObj = (object)entity;

                  this.db.Map<object>(query, r => {
                     mapper.Load(ref entityObj, r);
                     return null;

                  }).SingleOrDefault();
               }
            }
         }
      }

      /// <summary>
      /// Sets all mapped members of <paramref name="entity"/> to their most current persisted value.
      /// </summary>
      /// <param name="entity">The entity to refresh.</param>
      public void Refresh(TEntity entity) {
         Refresh(entity, null);
      }

      void Refresh(TEntity entity, IEnumerable<MetaDataMember> refreshMembers) {

         if (entity == null) throw new ArgumentNullException("entity");

         EnsureEntityType();

         IDictionary<string, object> predicateValues = metaType.IdentityMembers.ToDictionary(
            m => m.MappedName,
            m => m.MemberAccessor.GetBoxedValue(entity)
         );

         SqlBuilder query = this.SQL.SELECT_FROM(refreshMembers);
         query.WHERE(BuildPredicateFragment(predicateValues, query.ParameterValues));

         PocoMapper mapper = CreatePocoMapper();

         object entityObj = (object)entity;

         this.db.Map<object>(query, r => {
            mapper.Load(ref entityObj, r);
            return null;

         }).SingleOrDefault();
      }

      PocoMapper CreatePocoMapper() {
         return new PocoMapper(metaType.Type, this.db.Configuration.Log);
      }

      #region ISqlTable Members

      object ISqlTable.Find(object id) {
         return Find(id);
      }

      void ISqlTable.Insert(object entity) {
         Insert((TEntity)entity);
      }

      void ISqlTable.Insert(object entity, bool deep) {
         Insert((TEntity)entity, deep);
      }

      void ISqlTable.InsertDeep(object entity) {

#pragma warning disable 0618

         InsertDeep((TEntity)entity);

#pragma warning restore 0618
      }

      void ISqlTable.InsertDescendants(object entity) {
         InsertDescendants((TEntity)entity);
      }

      void ISqlTable.InsertRange(IEnumerable<object> entities) {
         InsertRange((IEnumerable<TEntity>)entities);
      }

      void ISqlTable.InsertRange(IEnumerable<object> entities, bool deep) {
         InsertRange((IEnumerable<TEntity>)entities, deep);
      }

      void ISqlTable.InsertRange(params object[] entities) {

         if (entities == null) throw new ArgumentNullException("entities");

         InsertRange(entities as TEntity[] ?? entities.Cast<TEntity>().ToArray());
      }

      void ISqlTable.InsertRange(object[] entities, bool deep) {

         if (entities == null) throw new ArgumentNullException("entities");

         InsertRange(entities as TEntity[] ?? entities.Cast<TEntity>().ToArray(), deep);
      }

      void ISqlTable.Update(object entity) {
         Update((TEntity)entity);
      }

      void ISqlTable.Update(object entity, ConcurrencyConflictPolicy conflictPolicy) {
         Update((TEntity)entity, conflictPolicy);
      }

      void ISqlTable.UpdateRange(IEnumerable<object> entities) {
         UpdateRange((IEnumerable<TEntity>)entities);
      }

      void ISqlTable.UpdateRange(IEnumerable<object> entities, ConcurrencyConflictPolicy conflictPolicy) {
         UpdateRange((IEnumerable<TEntity>)entities, conflictPolicy);
      }

      void ISqlTable.UpdateRange(params object[] entities) {

         if (entities == null) throw new ArgumentNullException("entities");

         UpdateRange(entities as TEntity[] ?? entities.Cast<TEntity>().ToArray());
      }

      void ISqlTable.UpdateRange(object[] entities, ConcurrencyConflictPolicy conflictPolicy) {

         if (entities == null) throw new ArgumentNullException("entities");

         UpdateRange(entities as TEntity[] ?? entities.Cast<TEntity>().ToArray(), conflictPolicy);
      }

      void ISqlTable.Delete(object entity) {
         Delete((TEntity)entity);
      }

      void ISqlTable.Delete(object entity, ConcurrencyConflictPolicy conflictPolicy) {
         Delete((TEntity)entity, conflictPolicy);
      }

      void ISqlTable.DeleteById(object id) {

#pragma warning disable 0618

         DeleteById(id);

#pragma warning restore 0618
      }

      void ISqlTable.DeleteById(object id, ConcurrencyConflictPolicy conflictPolicy) {

#pragma warning disable 0618

         DeleteById(id, conflictPolicy);

#pragma warning restore 0618
      }

      void ISqlTable.DeleteKey(object id) {
         DeleteKey(id);
      }

      void ISqlTable.DeleteKey(object id, ConcurrencyConflictPolicy conflictPolicy) {
         DeleteKey(id, conflictPolicy);
      }

      void ISqlTable.DeleteRange(IEnumerable<object> entities) {
         DeleteRange((IEnumerable<TEntity>)entities);
      }

      void ISqlTable.DeleteRange(IEnumerable<object> entities, ConcurrencyConflictPolicy conflictPolicy) {
         DeleteRange((IEnumerable<TEntity>)entities, conflictPolicy);
      }

      void ISqlTable.DeleteRange(params object[] entities) { 

         if (entities == null) throw new ArgumentNullException("entities");

         DeleteRange(entities as TEntity[] ?? entities.Cast<TEntity>().ToArray());
      }

      void ISqlTable.DeleteRange(object[] entities, ConcurrencyConflictPolicy conflictPolicy) {

         if (entities == null) throw new ArgumentNullException("entities");

         DeleteRange(entities as TEntity[] ?? entities.Cast<TEntity>().ToArray(), conflictPolicy);
      }

      bool ISqlTable.Contains(object entity) {
         return Contains((TEntity)entity);
      }

      bool ISqlTable.Contains(object entity, bool version) {
         return Contains((TEntity)entity, version);
      }

      void ISqlTable.Initialize(object entity) {

#pragma warning disable 0618

         Initialize((TEntity)entity);

#pragma warning restore 0618
      }

      void ISqlTable.Refresh(object entity) {
         Refresh((TEntity)entity);
      }

      #endregion
   }

   /// <summary>
   /// Generates SQL commands for entities mapped by <see cref="SqlTable"/> and <see cref="SqlTable&lt;TEntity>"/>.
   /// This class cannot be instantiated.
   /// </summary>
   /// <typeparam name="TEntity">The type of the entity to generate commands for.</typeparam>
   /// <seealso cref="SqlTable&lt;TEntity>.SQL"/>
   /// <seealso cref="SqlTable.SQL"/>
   public sealed class SqlCommandBuilder<TEntity> where TEntity : class {

      readonly Database db;
      readonly MetaType metaType;

      internal SqlCommandBuilder(Database db, MetaType metaType) {
         this.db = db;
         this.metaType = metaType;
      }

      string QuoteIdentifier(string unquotedIdentifier) {
         return this.db.QuoteIdentifier(unquotedIdentifier);
      }

      internal string BuildPredicateFragment(IDictionary<string, object> predicateValues, ICollection<object> parametersBuffer) {

         if (predicateValues == null || predicateValues.Count == 0) throw new ArgumentException("predicateValues cannot be empty", "predicateValues");
         if (parametersBuffer == null) throw new ArgumentNullException("parametersBuffer");

         var sb = new StringBuilder();

         foreach (var item in predicateValues) {
            if (sb.Length > 0) sb.Append(" AND ");

            sb.Append(QuoteIdentifier(item.Key));

            if (item.Value == null) {
               sb.Append(" IS NULL");
            } else {
               sb.Append(" = {")
                  .Append(parametersBuffer.Count)
                  .Append("}");

               parametersBuffer.Add(item.Value);
            }
         }

         return sb.ToString();
      }

      void EnsureEntityType() {
         SqlTable.EnsureEntityType(metaType);
      }

      /// <summary>
      /// Creates and returns a SELECT query for the current table
      /// that includes the SELECT clause only.
      /// </summary>
      /// <returns>The SELECT query for the current table.</returns>
      public SqlBuilder SELECT_() {
         return SELECT_(null);
      }

      /// <summary>
      /// Creates and returns a SELECT query for the current table
      /// that includes the SELECT clause only. All column names are qualified with the provided
      /// <paramref name="tableAlias"/>.
      /// </summary>
      /// <param name="tableAlias">The table alias.</param>
      /// <returns>The SELECT query for the current table.</returns>
      public SqlBuilder SELECT_(string tableAlias) {
         return SELECT_(null, tableAlias);
      }

      /// <summary>
      /// Creates and returns a SELECT query using the specified <paramref name="selectMembers"/>
      /// that includes the SELECT clause only. All column names are qualified with the provided
      /// <paramref name="tableAlias"/>.
      /// </summary>
      /// <param name="selectMembers">The members to use in the SELECT clause.</param>
      /// <param name="tableAlias">The table alias.</param>
      /// <returns>The SELECT query.</returns>
      internal SqlBuilder SELECT_(IEnumerable<MetaDataMember> selectMembers, string tableAlias) {
         return SqlTable.SELECT_(metaType, selectMembers, tableAlias, db);
      }

      /// <summary>
      /// Creates and returns a SELECT query for the current table
      /// that includes the SELECT and FROM clauses.
      /// </summary>
      /// <returns>The SELECT query for the current table.</returns>
      public SqlBuilder SELECT_FROM() {
         return SELECT_FROM((string)null);
      }

      /// <summary>
      /// Creates and returns a SELECT query for the current table
      /// that includes the SELECT and FROM clauses. All column names are qualified with the provided
      /// <paramref name="tableAlias"/>.
      /// </summary>
      /// <param name="tableAlias">The table alias.</param>
      /// <returns>The SELECT query for the current table.</returns>
      public SqlBuilder SELECT_FROM(string tableAlias) {
         return SELECT_FROM(null, tableAlias);
      }

      /// <summary>
      /// Creates and returns a SELECT query using the specified <paramref name="selectMembers"/>
      /// that includes the SELECT and FROM clauses.
      /// </summary>
      /// <param name="selectMembers">The members to use in the SELECT clause.</param>
      /// <returns>The SELECT query.</returns>
      internal SqlBuilder SELECT_FROM(IEnumerable<MetaDataMember> selectMembers) {
         return SELECT_FROM(selectMembers, null);
      }

      /// <summary>
      /// Creates and returns a SELECT query using the specified <paramref name="selectMembers"/>
      /// that includes the SELECT and FROM clauses. All column names are qualified with the provided
      /// <paramref name="tableAlias"/>.
      /// </summary>
      /// <param name="selectMembers">The members to use in the SELECT clause.</param>
      /// <param name="tableAlias">The table alias.</param>
      /// <returns>The SELECT query.</returns>
      internal SqlBuilder SELECT_FROM(IEnumerable<MetaDataMember> selectMembers, string tableAlias) {
         return SqlTable.SELECT_FROM(metaType, selectMembers, tableAlias, db);
      }

      /// <summary>
      /// Creates and returns an INSERT command for the specified <paramref name="entity"/>.
      /// </summary>
      /// <param name="entity">
      /// The object whose INSERT command is to be created. This parameter is named entity for consistency
      /// with the other CRUD methods, but in this case it doesn't need to be an actual entity, which means it doesn't
      /// need to have a primary key.
      /// </param>
      /// <returns>The INSERT command for <paramref name="entity"/>.</returns>
      public SqlBuilder INSERT_INTO_VALUES(TEntity entity) {

         if (entity == null) throw new ArgumentNullException("entity");

         MetaDataMember[] insertingMembers =
            (from m in metaType.PersistentDataMembers
             where !m.IsAssociation && !m.IsDbGenerated
             select m).ToArray();

         object[] parameters = insertingMembers.Select(m => m.MemberAccessor.GetBoxedValue(entity)).ToArray();

         var sb = new StringBuilder()
            .Append("INSERT INTO ")
            .Append(QuoteIdentifier(metaType.Table.TableName))
            .Append(" (");

         for (int i = 0; i < insertingMembers.Length; i++) {
            if (i > 0) sb.Append(", ");
            sb.Append(QuoteIdentifier(insertingMembers[i].MappedName));
         }

         sb.AppendLine(")")
            .Append("VALUES (");

         for (int i = 0; i < insertingMembers.Length; i++) {
            if (i > 0) sb.Append(", ");
            sb.Append("{")
               .Append(i)
               .Append("}");
         }

         sb.Append(")");

         return new SqlBuilder(sb.ToString(), parameters);
      }

      /// <summary>
      /// Creates and returns an UPDATE command for the current table
      /// that includes the UPDATE clause.
      /// </summary>
      /// <returns>The UPDATE command for the current table.</returns>
      public SqlBuilder UPDATE() {
         return new SqlBuilder("UPDATE " + QuoteIdentifier(metaType.Table.TableName));
      }

      /// <summary>
      /// Creates and returns an UPDATE command for the specified <paramref name="entity"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose UPDATE command is to be created.</param>
      /// <returns>The UPDATE command for <paramref name="entity"/>.</returns>
      public SqlBuilder UPDATE_SET_WHERE(TEntity entity) {
         return UPDATE_SET_WHERE(entity, this.db.Configuration.UpdateConflictPolicy);
      }

      /// <summary>
      /// Creates and returns an UPDATE command for the specified <paramref name="entity"/>
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose UPDATE command is to be created.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to include in the UPDATE predicate.
      /// </param>
      /// <returns>The UPDATE command for <paramref name="entity"/>.</returns>
      public SqlBuilder UPDATE_SET_WHERE(TEntity entity, ConcurrencyConflictPolicy conflictPolicy) {

         if (entity == null) throw new ArgumentNullException("entity");

         EnsureEntityType();

         MetaDataMember[] updatingMembers =
            (from m in metaType.PersistentDataMembers
             where !m.IsAssociation && !m.IsDbGenerated
             select m).ToArray();

         MetaDataMember[] predicateMembers =
            (from m in metaType.PersistentDataMembers
             where m.IsPrimaryKey || (m.IsVersion && conflictPolicy == ConcurrencyConflictPolicy.UseVersion)
             select m).ToArray();

         IDictionary<string, object> predicateValues = predicateMembers.ToDictionary(
            m => m.MappedName,
            m => m.MemberAccessor.GetBoxedValue(entity)
         );

         var parametersBuffer = new List<object>(updatingMembers.Length + predicateMembers.Length);

         var sb = new StringBuilder()
            .Append("UPDATE ")
            .Append(QuoteIdentifier(metaType.Table.TableName))
            .AppendLine()
            .Append("SET ");

         for (int i = 0; i < updatingMembers.Length; i++) {
            if (i > 0) sb.Append(", ");

            MetaDataMember member = updatingMembers[i];
            object value = member.MemberAccessor.GetBoxedValue(entity);

            sb.Append(QuoteIdentifier(member.MappedName))
               .Append(" = {")
               .Append(parametersBuffer.Count)
               .Append("}");

            parametersBuffer.Add(value);
         }

         sb.AppendLine()
            .Append("WHERE ")
            .Append(BuildPredicateFragment(predicateValues, parametersBuffer));

         return new SqlBuilder(sb.ToString(), parametersBuffer.ToArray());
      }

      /// <summary>
      /// Creates and returns a DELETE command for the current table
      /// that includes the DELETE and FROM clauses.
      /// </summary>
      /// <returns>The DELETE command for the current table.</returns>
      public SqlBuilder DELETE_FROM() {

         var sb = new StringBuilder()
            .Append("DELETE FROM ")
            .Append(QuoteIdentifier(metaType.Table.TableName));

         return new SqlBuilder(sb.ToString());
      }

      /// <summary>
      /// Creates and returns a DELETE command for the specified <paramref name="entity"/>,
      /// using the default <see cref="ConcurrencyConflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose DELETE command is to be created.</param>
      /// <returns>The DELETE command for <paramref name="entity"/>.</returns>
      public SqlBuilder DELETE_FROM_WHERE(TEntity entity) {
         return DELETE_FROM_WHERE(entity, this.db.Configuration.DeleteConflictPolicy);
      }

      /// <summary>
      /// Creates and returns a DELETE command for the specified <paramref name="entity"/>
      /// using the provided <paramref name="conflictPolicy"/>.
      /// </summary>
      /// <param name="entity">The entity whose DELETE command is to be created.</param>
      /// <param name="conflictPolicy">
      /// The <see cref="ConcurrencyConflictPolicy"/> that specifies what columns to include in the DELETE predicate.
      /// </param>
      /// <returns>The DELETE command for <paramref name="entity"/>.</returns>
      public SqlBuilder DELETE_FROM_WHERE(TEntity entity, ConcurrencyConflictPolicy conflictPolicy) {

         if (entity == null) throw new ArgumentNullException("entity");

         EnsureEntityType();

         MetaDataMember[] predicateMembers =
            (from m in metaType.PersistentDataMembers
             where m.IsPrimaryKey || (m.IsVersion && conflictPolicy == ConcurrencyConflictPolicy.UseVersion)
             select m).ToArray();

         IDictionary<string, object> predicateValues = predicateMembers.ToDictionary(
            m => m.MappedName,
            m => m.MemberAccessor.GetBoxedValue(entity)
         );

         var parametersBuffer = new List<object>();

         var sb = new StringBuilder()
            .Append("DELETE FROM ")
            .Append(QuoteIdentifier(metaType.Table.TableName))
            .AppendLine()
            .Append("WHERE (")
            .Append(BuildPredicateFragment(predicateValues, parametersBuffer))
            .Append(")");

         return new SqlBuilder(sb.ToString(), parametersBuffer.ToArray());
      }

      /// <summary>
      /// Creates and returns a DELETE command for the entity
      /// whose primary key matches the <paramref name="id"/> parameter.
      /// </summary>
      /// <param name="id">The primary key value.</param>
      /// <returns>The DELETE command the entity whose primary key matches the <paramref name="id"/> parameter.</returns>
      public SqlBuilder DELETE_FROM_WHERE_id(object id) {

         EnsureEntityType();

         if (metaType.IdentityMembers.Count > 1)
            throw new InvalidOperationException("Cannot call this method when the entity has more than one identity member.");

         return DELETE_FROM()
            .WHERE(QuoteIdentifier(metaType.IdentityMembers[0].MappedName) + " = {0}", id);
      }

      /// <summary>
      /// Returns whether the specified object is equal to the current object.
      /// </summary>
      /// <param name="obj">The object to compare with the current object. </param>
      /// <returns>True if the specified object is equal to the current object; otherwise, false.</returns>
      [EditorBrowsable(EditorBrowsableState.Never)]
      public override bool Equals(object obj) {
         return base.Equals(obj);
      }

      /// <summary>
      /// Returns the hash function for the current object.
      /// </summary>
      /// <returns>The hash function for the current object.</returns>
      [EditorBrowsable(EditorBrowsableState.Never)]
      public override int GetHashCode() {
         return base.GetHashCode();
      }

      /// <summary>
      /// Gets the type for the current object.
      /// </summary>
      /// <returns>The type for the current object.</returns>
      [EditorBrowsable(EditorBrowsableState.Never)]
      [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate", Justification = "Must match base signature.")]
      public new Type GetType() {
         return base.GetType();
      }

      /// <summary>
      /// Returns a string representation of the object.
      /// </summary>
      /// <returns>A string representation of the object.</returns>
      [EditorBrowsable(EditorBrowsableState.Never)]
      public override string ToString() {
         return base.ToString();
      }
   }

   interface ISqlTable {

      bool Contains(object entity);
      bool Contains(object entity, bool version);
      bool ContainsKey(object id);
      
      void Delete(object entity);
      void Delete(object entity, ConcurrencyConflictPolicy conflictPolicy);
      void DeleteKey(object id);
      void DeleteKey(object id, ConcurrencyConflictPolicy conflictPolicy);
      void DeleteRange(IEnumerable<object> entities);
      void DeleteRange(IEnumerable<object> entities, ConcurrencyConflictPolicy conflictPolicy);
      void DeleteRange(params object[] entities);
      void DeleteRange(object[] entities, ConcurrencyConflictPolicy conflictPolicy);

      void DeleteById(object id); // deprecated
      void DeleteById(object id, ConcurrencyConflictPolicy conflictPolicy); // deprecated
      
      object Find(object id);
      void Initialize(object entity); // deprecated

      void Insert(object entity);
      void Insert(object entity, bool deep);
      
      void InsertDeep(object entity); // deprecated
      void InsertDescendants(object entity); // internal
      
      void InsertRange(IEnumerable<object> entities);
      void InsertRange(IEnumerable<object> entities, bool deep);
      void InsertRange(params object[] entities);
      void InsertRange(object[] entities, bool deep);

      void Refresh(object entity);

      void Update(object entity);
      void Update(object entity, ConcurrencyConflictPolicy conflictPolicy);
      void UpdateRange(IEnumerable<object> entities);
      void UpdateRange(IEnumerable<object> entities, ConcurrencyConflictPolicy conflictPolicy);
      void UpdateRange(params object[] entities);
      void UpdateRange(object[] entities, ConcurrencyConflictPolicy conflictPolicy);
   }
}
