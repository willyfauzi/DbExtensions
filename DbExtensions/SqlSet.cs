﻿// Copyright 2012 Max Toro Q.
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
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections;

namespace DbExtensions {

   /// <summary>
   /// Represents an immutable, connected SQL query.
   /// </summary>
   [DebuggerDisplay("{definingQuery}")]
   public class SqlSet : ISqlSet<SqlSet, object> {

      readonly SqlBuilder definingQuery;
      readonly Type resultType;
      readonly ISqlSetContext context;
      readonly int setIndex = 1;

      // OrderBy and Skip calls are buffered for the following reasons:
      // - Append LIMIT and OFFSET as one clause (MySQL, SQLite), in the appropiate order
      //   e.g. Skip(x).Take(y) -> LIMIT y OFFSET x
      // - Append ORDER BY, OFFSET and FETCH as one clause (for SQL Server)
      // - Minimize the number of subqueries

      SqlFragment orderByBuffer;
      int? skipBuffer;

      /// <summary>
      /// The database connection.
      /// </summary>
      public DbConnection Connection { 
         get { return context.Connection; } 
      }

      /// <summary>
      /// A <see cref="TextWriter"/> used to log when queries are executed.
      /// </summary>
      protected internal TextWriter Log {
         get { return context.Log; }
      }

      private bool HasBufferedCalls {
         get {
            return skipBuffer.HasValue
               || orderByBuffer != null;
         }
      }

      /// <summary>
      /// Initializes a new instance of the <see cref="SqlSet"/> class
      /// using the provided defining query.
      /// </summary>
      /// <param name="definingQuery">The SQL query that will be the source of data for the set.</param>
      public SqlSet(SqlBuilder definingQuery) 
         : this(definingQuery, DbFactory.CreateConnection()) { }

      /// <summary>
      /// Initializes a new instance of the <see cref="SqlSet"/> class
      /// using the provided defining query and connection.
      /// </summary>
      /// <param name="definingQuery">The SQL query that will be the source of data for the set.</param>
      /// <param name="connection">The database connection.</param>
      public SqlSet(SqlBuilder definingQuery, DbConnection connection) 
         : this(definingQuery, connection, null) { }

      /// <summary>
      /// Initializes a new instance of the <see cref="SqlSet"/> class
      /// using the provided defining query, connection and logger.
      /// </summary>
      /// <param name="definingQuery">The SQL query that will be the source of data for the set.</param>
      /// <param name="connection">The database connection.</param>
      /// <param name="logger">A <see cref="TextWriter"/> used to log when queries are executed.</param>
      public SqlSet(SqlBuilder definingQuery, DbConnection connection, TextWriter logger)
         : this(definingQuery, null, connection, logger) { }

      /// <summary>
      /// Initializes a new instance of the <see cref="SqlSet"/> class
      /// using the provided defining query and result type.
      /// </summary>
      /// <param name="definingQuery">The SQL query that will be the source of data for the set.</param>
      /// <param name="resultType">The type of objects to map the results to.</param>
      public SqlSet(SqlBuilder definingQuery, Type resultType)
         : this(definingQuery, resultType, DbFactory.CreateConnection()) { }

      /// <summary>
      /// Initializes a new instance of the <see cref="SqlSet"/> class
      /// using the provided defining query, result type and connection.
      /// </summary>
      /// <param name="definingQuery">The SQL query that will be the source of data for the set.</param>
      /// <param name="resultType">The type of objects to map the results to.</param>
      /// <param name="connection">The database connection.</param>
      public SqlSet(SqlBuilder definingQuery, Type resultType, DbConnection connection) 
         : this(definingQuery, resultType, connection, null) { }

      /// <summary>
      /// Initializes a new instance of the <see cref="SqlSet"/> class
      /// using the provided defining query, result type, connection and logger.
      /// </summary>
      /// <param name="definingQuery">The SQL query that will be the source of data for the set.</param>
      /// <param name="resultType">The type of objects to map the results to.</param>
      /// <param name="connection">The database connection.</param>
      /// <param name="logger">A <see cref="TextWriter"/> used to log when queries are executed.</param>
      public SqlSet(SqlBuilder definingQuery, Type resultType, DbConnection connection, TextWriter logger)
         : this(definingQuery, resultType, connection, logger, adoptQuery: false) { }

      internal SqlSet(SqlBuilder definingQuery, Type resultType, DbConnection connection, TextWriter logger, bool adoptQuery) 
         : this(definingQuery, resultType, new SqlSetDefaultContext(connection, logger), adoptQuery) { }

      internal SqlSet(SqlBuilder definingQuery, Type resultType, ISqlSetContext context, bool adoptQuery) {

         if (definingQuery == null) throw new ArgumentNullException("definingQuery");

         this.definingQuery = (adoptQuery) ?
            definingQuery
            : definingQuery.Clone();

         this.resultType = resultType;
         this.context = context;
      }

      protected SqlSet(SqlSet set, SqlBuilder superQuery) {

         if (set == null) throw new ArgumentNullException("set");
         if (superQuery == null) throw new ArgumentNullException("superQuery");

         this.definingQuery = superQuery;
         this.resultType = set.resultType;
         this.setIndex += set.setIndex;
         this.context = set.context;
      }

      protected SqlSet(SqlSet set, SqlBuilder superQuery, Type resultType)
         : this(set, superQuery) {

         this.resultType = resultType;
      }

      [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate", Justification = "Calling the member twice in succession creates different results.")]
      public SqlBuilder GetDefiningQuery() {
         return GetDefiningQuery(clone: true);
      }

      SqlBuilder GetDefiningQuery(bool clone = true, bool omitBufferedCalls = false) {

         bool applyBuffer = this.HasBufferedCalls && !omitBufferedCalls;
         bool shouldClone = clone || !applyBuffer;

         SqlBuilder query = this.definingQuery;

         if (shouldClone)
            query = query.Clone();

         if (applyBuffer) {

            query = CreateSuperQuery(query, null, null);

            ApplyOrderBySkipTake(query, this.orderByBuffer, this.skipBuffer);
         }

         return query;
      }

      void ApplyOrderBySkipTake(SqlBuilder query, SqlFragment orderBy, int? skip, int? take = null) {

         bool hasOrderBy = orderBy != null;
         bool hasSkip = skip.HasValue;
         bool hasTake = take.HasValue;

         if (hasOrderBy)
            query.ORDER_BY(orderBy.Format, orderBy.Args);

         if (IsSqlServer()) {

            bool useFetch = hasSkip && hasTake;
            bool usingTop = hasTake && !useFetch;

            if (!hasOrderBy && hasSkip) {

               // Cannot have OFFSET without ORDER BY
               query.ORDER_BY("1");
            }

            if (hasSkip) {
               query.OFFSET(skip.Value.ToString(CultureInfo.InvariantCulture) + " ROWS");

            } else if (hasOrderBy && !usingTop) {

               // The ORDER BY clause is invalid in subqueries, unless TOP, OFFSET or FOR XML is also specified.

               query.OFFSET("0 ROWS");
            }

            if (useFetch)
               query.AppendClause("FETCH", null, String.Concat("NEXT ", take.Value.ToString(CultureInfo.InvariantCulture), " ROWS ONLY"), null);
         
         } else {

            if (hasTake)
               query.LIMIT(take.Value);

            if (hasSkip)
               query.OFFSET(skip.Value);
         }
      }

      void CopyBufferState(SqlSet otherSet) {

         otherSet.orderByBuffer = this.orderByBuffer;
         otherSet.skipBuffer = this.skipBuffer;
      }

      bool IsSqlServer() {
         return this.Connection is System.Data.SqlClient.SqlConnection
            || this.Connection.GetType().Namespace.Equals("System.Data.SqlServerCe", StringComparison.Ordinal);
      }

      protected SqlBuilder CreateSuperQuery() {
         return CreateSuperQuery(null, null);
      }

      protected SqlBuilder CreateSuperQuery(string selectFormat, params object[] args) {
         return CreateSuperQuery(GetDefiningQuery(clone: false), selectFormat, args);
      }

      SqlBuilder CreateSuperQuery(SqlBuilder definingQuery, string selectFormat, object[] args) {

         var query = new SqlBuilder()
            .SELECT(selectFormat ?? "*", args)
            .FROM(definingQuery, "__set" + this.setIndex.ToString(CultureInfo.InvariantCulture));

         return query;
      }

      protected virtual SqlSet CreateSet(SqlBuilder superQuery) {
         return new SqlSet(this, superQuery);
      }

      protected virtual SqlSet CreateSet(SqlBuilder superQuery, Type resultType) {
         return new SqlSet(this, superQuery, resultType);
      }

      protected virtual SqlSet<TResult> CreateSet<TResult>(SqlBuilder superQuery) {
         return new SqlSet<TResult>(this, superQuery);
      }

      protected virtual SqlSet<TResult> CreateSet<TResult>(SqlBuilder superQuery, Func<IDataRecord, TResult> mapper) {
         return new SqlSet<TResult>(this, superQuery, mapper);
      }

      protected DbCommand CreateCommand(SqlBuilder sqlBuilder) {
         return this.context.CreateCommand(sqlBuilder);
      }

      protected virtual IEnumerable Execute(DbCommand command) {

         if (this.resultType == null)
            throw new InvalidOperationException("Cannot map set, a result type was not specified when this set was created. Call the 'Cast' method first.");

         return command.Map(resultType, this.Log);
      }

      #region ISqlSet<SqlSet,object> Members

      /// <summary>
      /// Determines whether all elements of the set satisfy a condition.
      /// </summary>
      /// <param name="predicate">A SQL expression to test each row for a condition.</param>
      /// <returns>true if every element of the set passes the test in the specified predicate, or if the set is empty; otherwise, false.</returns>
      public bool All(string predicate) {
         return All(predicate, null);
      }

      /// <summary>
      /// Determines whether all elements of the set satisfy a condition.
      /// </summary>
      /// <param name="predicate">A SQL expression to test each row for a condition.</param>
      /// <param name="parameters">The parameters to apply to the predicate.</param>
      /// <returns>true if every element of the set passes the test in the specified predicate, or if the set is empty; otherwise, false.</returns>
      public bool All(string predicate, params object[] parameters) {

         if (predicate == null) throw new ArgumentNullException("predicate");

         predicate = String.Concat("NOT (", predicate, ")");

         return !Any(predicate, parameters);
      }

      /// <summary>
      /// Determines whether the set contains any elements.
      /// </summary>
      /// <returns>true if the sequence contains any elements; otherwise, false.</returns>
      public bool Any() {
         return this.Connection.Exists(CreateCommand(Extensions.ExistsQuery(GetDefiningQuery(clone: false))), this.Log);
      }

      /// <summary>
      /// Determines whether any element of the set satisfies a condition.
      /// </summary>
      /// <param name="predicate">A SQL expression to test each row for a condition.</param>
      /// <returns>true if any elements in the set pass the test in the specified predicate; otherwise, false.</returns>
      public bool Any(string predicate) {
         return Where(predicate).Any();
      }

      /// <summary>
      /// Determines whether any element of the set satisfies a condition.
      /// </summary>
      /// <param name="predicate">A SQL expression to test each row for a condition.</param>
      /// <param name="parameters">The parameters to apply to the predicate.</param>
      /// <returns>true if any elements in the set pass the test in the specified predicate; otherwise, false.</returns>
      public bool Any(string predicate, params object[] parameters) {
         return Where(predicate, parameters).Any();
      }

      /// <summary>
      /// Gets all elements in the set. The query is deferred-executed.
      /// </summary>
      /// <returns>All elements in the set.</returns>
      public IEnumerable<object> AsEnumerable() {
         return (IEnumerable<object>)Execute(CreateCommand(GetDefiningQuery(clone: false)));
      }

      public SqlSet<TResult> Cast<TResult>() {

         if (this.resultType != null
            && this.resultType != typeof(TResult)) {
            throw new InvalidOperationException("The specified type parameter is not valid for this instance.");
         }

         return CreateSet<TResult>(GetDefiningQuery());
      }

      public SqlSet Cast(Type resultType) {

         if (this.resultType != null
            && this.resultType != resultType) {
            throw new InvalidOperationException("The specified type parameter is not valid for this instance.");
         }

         return CreateSet(GetDefiningQuery(), resultType);
      }

      /// <summary>
      /// Returns the number of elements in the set.
      /// </summary>
      /// <returns>The number of elements in the set.</returns>
      /// <exception cref="System.OverflowException">The number of elements in the set is larger than <see cref="Int32.MaxValue"/>.</exception>
      public int Count() {
         return this.Connection.Count(CreateCommand(Extensions.CountQuery(GetDefiningQuery(clone: false))), this.Log);
      }

      /// <summary>
      /// Returns a number that represents how many elements in the set satisfy a condition.
      /// </summary>
      /// <param name="predicate">A SQL expression to test each row for a condition.</param>
      /// <returns>A number that represents how many elements in the set satisfy the condition in the predicate.</returns>
      /// <exception cref="System.OverflowException">The number of elements in the set is larger than <see cref="Int32.MaxValue"/>.</exception>      
      public int Count(string predicate) {
         return Where(predicate).Count();
         /// <exception cref="System.OverflowException">The number of rows in the set is larger than <see cref="Int32.MaxValue"/>.</exception>
      }

      /// <summary>
      /// Gets the number of elements in the set that matches the <paramref name="predicate"/>.
      /// </summary>
      /// <param name="predicate">A SQL expression to test each row for a condition.</param>
      /// <param name="parameters">The parameters to apply to the predicate.</param>
      /// <returns>The number of elements that match the <paramref name="predicate"/>.</returns>
      /// <exception cref="System.OverflowException">The number of elements in the set is larger than <see cref="Int32.MaxValue"/>.</exception>      
      public int Count(string predicate, params object[] parameters) {
         return Where(predicate, parameters).Count();
      }

      /// <summary>
      /// Returns the first element of the set.
      /// </summary>
      /// <returns>The first element in the set.</returns>
      /// <exception cref="System.InvalidOperationException">The set is empty.</exception>
      public object First() {
         return Take(1).AsEnumerable().First();
      }

      /// <summary>
      /// Returns the first element in the set that satisfies a specified condition.
      /// </summary>
      /// <param name="predicate">A SQL expression to test each row for a condition.</param>
      /// <returns>The first element in the set that passes the test in the specified predicate.</returns>
      /// <exception cref="System.InvalidOperationException">No element satisfies the condition in predicate.-or-The set is empty.</exception>
      public object First(string predicate) {
         return Where(predicate).First();
      }

      /// <summary>
      /// Returns the first element in the set that satisfies a specified condition.
      /// </summary>
      /// <param name="predicate">A SQL expression to test each row for a condition.</param>
      /// <param name="parameters">The parameters to apply to the predicate.</param>
      /// <returns>The first element in the set that passes the test in the specified predicate.</returns>
      /// <exception cref="System.InvalidOperationException">No element satisfies the condition in predicate.-or-The set is empty.</exception>
      public object First(string predicate, params object[] parameters) {
         return Where(predicate, parameters).First();
      }

      public object FirstOrDefault() {
         return Take(1).AsEnumerable().FirstOrDefault();
      }

      public object FirstOrDefault(string predicate) {
         return Where(predicate).FirstOrDefault();
      }

      public object FirstOrDefault(string predicate, params object[] parameters) {
         return Where(predicate, parameters).FirstOrDefault();
      }

      public IEnumerator<object> GetEnumerator() {
         return AsEnumerable().GetEnumerator();
      }

      [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "long", Justification = "Consistent with LINQ.")]
      public long LongCount() {
         return this.Connection.LongCount(CreateCommand(Extensions.CountQuery(GetDefiningQuery(clone: false))), this.Log);
      }

      public long LongCount(string predicate) {
         return Where(predicate).LongCount();
      }

      /// <summary>
      /// Gets the number of rows in the set that matches the <paramref name="predicate"/>.
      /// </summary>
      /// <param name="predicate">The SQL predicate.</param>
      /// <param name="parameters">The parameters to use in the predicate.</param>
      /// <returns>The number of rows that match the <paramref name="predicate"/>.</returns>
      [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "long", Justification = "Consistent with LINQ.")]
      public long LongCount(string predicate, params object[] parameters) {
         return Where(predicate, parameters).LongCount();
      }

      public SqlSet OrderBy(string format) {
         return OrderBy(format, null);
      }

      public SqlSet OrderBy(string format, params object[] args) {

         SqlBuilder query = (this.orderByBuffer == null) ?
            GetDefiningQuery(omitBufferedCalls: true)
            : CreateSuperQuery();

         SqlSet set = CreateSet(query);
         CopyBufferState(set);
         set.orderByBuffer = new SqlFragment(format, args);

         return set;
      }

      public SqlSet<TResult> Select<TResult>(string format) {
         return Select<TResult>(format, null);
      }

      public SqlSet<TResult> Select<TResult>(string format, params object[] args) {

         var superQuery = CreateSuperQuery(format, args);

         return CreateSet<TResult>(superQuery);
      }

      public SqlSet<TResult> Select<TResult>(Func<IDataRecord, TResult> mapper, string format) {
         return Select<TResult>(mapper, format, null);
      }

      public SqlSet<TResult> Select<TResult>(Func<IDataRecord, TResult> mapper, string format, params object[] args) {

         var superQuery = CreateSuperQuery(format, args);

         return CreateSet<TResult>(superQuery, mapper);
      }

      public SqlSet Select(Type resultType, string format) {
         return Select(resultType, format, null);
      }

      public SqlSet Select(Type resultType, string format, params object[] args) {

         var superQuery = CreateSuperQuery(format, args);

         return CreateSet(superQuery, resultType);
      }

      public object Single() {
         return AsEnumerable().Single();
      }

      public object Single(string predicate) {
         return Where(predicate).Single();
      }

      public object Single(string predicate, params object[] parameters) {
         return Where(predicate, parameters).Single();
      }

      public object SingleOrDefault() {
         return AsEnumerable().SingleOrDefault();
      }

      public object SingleOrDefault(string predicate) {
         return Where(predicate).SingleOrDefault();
      }

      public object SingleOrDefault(string predicate, params object[] parameters) {
         return Where(predicate, parameters).SingleOrDefault();
      }

      public SqlSet Skip(int count) {

         SqlBuilder query = (!this.skipBuffer.HasValue) ?
            GetDefiningQuery(omitBufferedCalls: true)
            : CreateSuperQuery();

         SqlSet set = CreateSet(query);
         CopyBufferState(set);
         set.skipBuffer = count;

         return set;
      }

      public SqlSet Take(int count) {

         SqlBuilder query;

         if (this.HasBufferedCalls) {
            
            query = GetDefiningQuery(omitBufferedCalls: true);

            if (IsSqlServer() && !this.skipBuffer.HasValue) {
               query = CreateSuperQuery(query, String.Concat("TOP(", count.ToString(CultureInfo.InvariantCulture), ") *"), null);
               ApplyOrderBySkipTake(query, this.orderByBuffer, skip: null, take: count);

            } else {
               query = CreateSuperQuery(query, null, null);
               ApplyOrderBySkipTake(query, this.orderByBuffer, this.skipBuffer, take: count);
            }

         } else {

            if (IsSqlServer()) {
               query = CreateSuperQuery(String.Concat("TOP(", count.ToString(CultureInfo.InvariantCulture), ") *"), null);

            } else {
               query = CreateSuperQuery();
               ApplyOrderBySkipTake(query, orderBy: null, skip: null, take: count);
            }
         }

         return CreateSet(query);
      }

      public object[] ToArray() {
         return AsEnumerable().ToArray();
      }

      [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists", Justification = "Consistent with LINQ.")]
      public List<object> ToList() {
         return AsEnumerable().ToList();
      }

      public SqlSet Where(string predicate) {
         return Where(predicate, null);
      }

      public SqlSet Where(string predicate, params object[] parameters) {

         var superQuery = CreateSuperQuery()
            .WHERE(predicate, parameters);

         return CreateSet(superQuery);
      }

      public SqlSet Union(SqlSet otherSet) {

         if (otherSet == null) throw new ArgumentNullException("otherSet");

         // TODO: check result compatibility?

         var superQuery = CreateSuperQuery()
            .UNION()
            .Append(otherSet.CreateSuperQuery());

         return CreateSet(superQuery);
      }

      #endregion

      #region Object Members

      /// <summary>
      /// Returns whether the specified set is equal to the current set.
      /// </summary>
      /// <param name="obj">The set to compare with the current set. </param>
      /// <returns>True if the specified set is equal to the current set; otherwise, false.</returns>
      [EditorBrowsable(EditorBrowsableState.Never)]
      public override bool Equals(object obj) {
         return base.Equals(obj);
      }

      /// <summary>
      /// Returns the hash function for the current set.
      /// </summary>
      /// <returns>The hash function for the current set.</returns>
      [EditorBrowsable(EditorBrowsableState.Never)]
      public override int GetHashCode() {
         return base.GetHashCode();
      }

      /// <summary>
      /// Gets the type for the current set.
      /// </summary>
      /// <returns>The type for the current set.</returns>
      [EditorBrowsable(EditorBrowsableState.Never)]
      [SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate", Justification = "Must match base signature.")]
      public new Type GetType() {
         return base.GetType();
      }

      /// <summary>
      /// Returns the SQL query of the set.
      /// </summary>
      /// <returns>The SQL query of the set.</returns>
      public override string ToString() {
         return GetDefiningQuery(clone: false).ToString();
      }

      #endregion

      #region Nested Types

      sealed class SqlFragment {
         public readonly string Format;
         public readonly object[] Args;

         public SqlFragment(string format, object[] args) {
            this.Format = format;
            this.Args = args;
         }
      }

      #endregion
   }

   public class SqlSet<TResult> : SqlSet, ISqlSet<SqlSet<TResult>, TResult> {

      readonly Func<IDataRecord, TResult> mapper;

      public SqlSet(SqlBuilder definingQuery)
         : base(definingQuery, typeof(TResult)) { }

      public SqlSet(SqlBuilder definingQuery, DbConnection connection) 
         : base(definingQuery, typeof(TResult), connection) { }

      public SqlSet(SqlBuilder definingQuery, DbConnection connection, TextWriter logger)
         : base(definingQuery, typeof(TResult), connection, logger) { }

      internal SqlSet(SqlBuilder definingQuery, ISqlSetContext context, bool adoptQuery)
         : base(definingQuery, typeof(TResult), context, adoptQuery) { }

      public SqlSet(SqlBuilder definingQuery, Func<IDataRecord, TResult> mapper)
         : this(definingQuery, mapper, DbFactory.CreateConnection()) { }

      public SqlSet(SqlBuilder definingQuery, Func<IDataRecord, TResult> mapper, DbConnection connection) 
         : this(definingQuery, mapper, connection, null) { }

      public SqlSet(SqlBuilder definingQuery, Func<IDataRecord, TResult> mapper, DbConnection connection, TextWriter logger)
         : base(definingQuery, typeof(TResult), connection, logger) {

         if (mapper == null) throw new ArgumentNullException("mapper");

         this.mapper = mapper;
      }

      internal SqlSet(SqlBuilder definingQuery, Func<IDataRecord, TResult> mapper, ISqlSetContext context, bool adoptQuery) 
         : base(definingQuery, typeof(TResult), context, adoptQuery) {

         if (mapper == null) throw new ArgumentNullException("mapper");

         this.mapper = mapper;
      }

      protected SqlSet(SqlSet<TResult> set, SqlBuilder superQuery) 
         : base((SqlSet)set, superQuery) {

         if (set == null) throw new ArgumentNullException("set");

         this.mapper = set.mapper;
      }

      // These constructors are used by SqlSet

      internal SqlSet(SqlSet set, SqlBuilder superQuery)
         : base(set, superQuery, typeof(TResult)) { }

      internal SqlSet(SqlSet set, SqlBuilder superQuery, Func<IDataRecord, TResult> mapper)
         : base(set, superQuery, typeof(TResult)) {

         if (mapper == null) throw new ArgumentNullException("mapper");

         this.mapper = mapper;
      }

      protected override SqlSet CreateSet(SqlBuilder superQuery) {
         return new SqlSet<TResult>(this, superQuery);
      }

      protected override SqlSet<TResult2> CreateSet<TResult2>(SqlBuilder superQuery) {
         return new SqlSet<TResult2>(this, superQuery);
      }

      protected override IEnumerable Execute(DbCommand command) {

         if (this.mapper != null)
            return command.Map(this.mapper, this.Log);

         return command.Map<TResult>(this.Log);
      }

      #region ISqlSet<SqlSet<TResult>,TResult> Members

      /// <summary>
      /// Gets all <typeparamref name="TResult"/> objects in the set. The query is deferred-executed.
      /// </summary>
      /// <returns>All <typeparamref name="TResult"/> objects in the set.</returns>
      public new IEnumerable<TResult> AsEnumerable() {
         return (IEnumerable<TResult>)base.AsEnumerable();
      }

      public new TResult First() {
         return Take(1).AsEnumerable().First();
      }

      public new TResult First(string predicate) {
         return Where(predicate).First();
      }

      public new TResult First(string predicate, params object[] parameters) {
         return Where(predicate, parameters).First();
      }

      public new TResult FirstOrDefault() {
         return Take(1).AsEnumerable().FirstOrDefault();
      }

      public new TResult FirstOrDefault(string predicate) {
         return Where(predicate).FirstOrDefault();
      }

      public new TResult FirstOrDefault(string predicate, params object[] parameters) {
         return Where(predicate, parameters).FirstOrDefault();
      }

      public new IEnumerator<TResult> GetEnumerator() {
         return AsEnumerable().GetEnumerator();
      }

      public new SqlSet<TResult> OrderBy(string format) {
         return (SqlSet<TResult>)base.OrderBy(format);
      }

      public new SqlSet<TResult> OrderBy(string format, params object[] args) {
         return (SqlSet<TResult>)base.OrderBy(format, args);
      }

      public new TResult Single() {
         return AsEnumerable().Single();
      }

      public new TResult Single(string predicate) {
         return Where(predicate).Single();
      }

      public new TResult Single(string predicate, params object[] parameters) {
         return Where(predicate, parameters).Single();
      }

      public new TResult SingleOrDefault() {
         return AsEnumerable().SingleOrDefault();
      }

      public new TResult SingleOrDefault(string predicate) {
         return Where(predicate).SingleOrDefault();
      }

      public new TResult SingleOrDefault(string predicate, params object[] parameters) {
         return Where(predicate, parameters).SingleOrDefault();
      }

      public new SqlSet<TResult> Skip(int count) {
         return (SqlSet<TResult>)base.Skip(count);
      }

      public new SqlSet<TResult> Take(int count) {
         return (SqlSet<TResult>)base.Take(count);
      }

      public new TResult[] ToArray() {
         return AsEnumerable().ToArray();
      }

      [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists", Justification = "Consistent with LINQ.")]
      public new List<TResult> ToList() {
         return AsEnumerable().ToList();
      }

      public new SqlSet<TResult> Where(string predicate) {
         return (SqlSet<TResult>)base.Where(predicate);
      }

      public new SqlSet<TResult> Where(string predicate, params object[] parameters) {
         return (SqlSet<TResult>)base.Where(predicate, parameters);
      }

      public SqlSet<TResult> Union(SqlSet<TResult> otherSet) {
         return (SqlSet<TResult>)base.Union(otherSet);
      }

      #endregion
   }
   
   interface ISqlSet<TSqlSet, TSource> where TSqlSet : SqlSet {

      bool All(string predicate);
      bool All(string predicate, params object[] parameters);
      bool Any();
      bool Any(string predicate);
      bool Any(string predicate, params object[] parameters);
      IEnumerable<TSource> AsEnumerable();
      SqlSet<TResult> Cast<TResult>();
      SqlSet Cast(Type resultType);
      int Count();
      int Count(string predicate);
      int Count(string predicate, params object[] parameters);
      TSource First();
      TSource First(string predicate);
      TSource First(string predicate, params object[] parameters);
      TSource FirstOrDefault();
      TSource FirstOrDefault(string predicate);
      TSource FirstOrDefault(string predicate, params object[] parameters);
      IEnumerator<TSource> GetEnumerator();
      long LongCount();
      long LongCount(string predicate);
      long LongCount(string predicate, params object[] parameters);
      TSqlSet OrderBy(string format);
      TSqlSet OrderBy(string format, params object[] args);
      SqlSet<TResult> Select<TResult>(string format);
      SqlSet<TResult> Select<TResult>(string format, params object[] args);
      SqlSet<TResult> Select<TResult>(Func<IDataRecord, TResult> mapper, string format);
      SqlSet<TResult> Select<TResult>(Func<IDataRecord, TResult> mapper, string format, params object[] args);
      SqlSet Select(Type resultType, string format);
      SqlSet Select(Type resultType, string format, params object[] args);
      TSource Single();
      TSource Single(string predicate);
      TSource Single(string predicate, params object[] parameters);
      TSource SingleOrDefault();
      TSource SingleOrDefault(string predicate);
      TSource SingleOrDefault(string predicate, params object[] parameters);
      TSqlSet Skip(int count);
      TSqlSet Take(int count);
      TSource[] ToArray();
      List<TSource> ToList();
      TSqlSet Where(string predicate);
      TSqlSet Where(string predicate, params object[] parameters);
      TSqlSet Union(TSqlSet otherSet);
   }

   interface ISqlSetContext {

      DbConnection Connection { get; }
      TextWriter Log { get; }

      DbCommand CreateCommand(SqlBuilder query);
   }

   sealed class SqlSetDefaultContext : ISqlSetContext {

      readonly DbConnection connection;
      readonly TextWriter log;

      public DbConnection Connection {
         get { return connection; }
      }

      public TextWriter Log {
         get { return log; }
      }

      public SqlSetDefaultContext(DbConnection connection, TextWriter log = null) {

         if (connection == null) throw new ArgumentNullException("connection");

         this.connection = connection;
         this.log = log;
      }

      public DbCommand CreateCommand(SqlBuilder query) {
         return query.ToCommand(this.connection);
      }
   }
}
