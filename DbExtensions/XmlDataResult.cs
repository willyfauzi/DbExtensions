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
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;

namespace DbExtensions {

   partial class SqlSet {

      public XmlReader AsXml() {
         return AsXml(null, null);
      }

      public XmlReader AsXml(XmlQualifiedName collectionName, string itemName) {
         return AsXml(collectionName, itemName, XmlNullHandling.OmitElement, XmlTypeAnnotation.None);
      }

      /// <summary>
      /// Returns an <see cref="XmlReader"/> object that provides an XML view of the set's data. 
      /// </summary>
      /// <param name="collectionName">The qualified name of the outermost element. The default is 'table'.</param>
      /// <param name="itemName">
      /// The local name of the elements that represent rows returned by the query. 
      /// The elements inherit the namespace specified by <paramref name="collectionName"/>. The default is 'row'.
      /// </param>
      /// <param name="nullHandling">Specifies how to handle null fields. The default is <see cref="XmlNullHandling.OmitElement"/>.</param>
      /// <param name="typeAnnotation">Specifies what kind of type information to include. The default is <see cref="XmlTypeAnnotation.None"/>.</param>
      /// <returns>An <see cref="XmlReader"/>.</returns>
      [SuppressMessage("Microsoft.Design", "CA1026:DefaultParametersShouldNotBeUsed", Justification = "Other overloads are provided. For programming languages that do support default parameters this method can be called with any combination of arguments.")]
      public XmlReader AsXml(
         XmlQualifiedName collectionName = null,
         string itemName = null,
         XmlNullHandling nullHandling = XmlNullHandling.OmitElement,
         XmlTypeAnnotation typeAnnotation = XmlTypeAnnotation.None) {
         
         return new XmlMappingReader(
            this.Connection.Map<IDataRecord>(GetDefiningQuery(clone: true), r => r, this.Log).GetEnumerator(),
            settings: null,
            tableName: collectionName,
            rowName: itemName,
            xsiNil: nullHandling == XmlNullHandling.IncludeNilAttribute,
            xsiType: typeAnnotation == XmlTypeAnnotation.XmlSchema
         );
      }
   }

   /// <summary>
   /// Specifies how <see cref="SqlSet.AsXml()"/> should handle null fields.
   /// </summary>
   public enum XmlNullHandling {
      
      /// <summary>
      /// Omits the element.
      /// </summary>
      OmitElement,

      /// <summary>
      /// Adds an empty element with an xsi:nil="true" attribute.
      /// </summary>
      IncludeNilAttribute
   }

   /// <summary>
   /// Specifies what kind of type information should <see cref="SqlSet.AsXml()"/> include.
   /// </summary>
   public enum XmlTypeAnnotation {

      /// <summary>
      /// Don't include type information.
      /// </summary>
      None,

      /// <summary>
      /// Include xsi:type attribute and map the CLR type to an XML Schema type.
      /// </summary>
      XmlSchema
   }

   sealed class XmlMappingReader : XmlReader {

      const string xsPrefix = "xs";
      const string xsiPrefix = "xsi";
      const string xsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";
      const string defaultRowName = "row";
      const string defaultFieldName = "field";
      static readonly XmlQualifiedName defaultTableName = new XmlQualifiedName("table");
      static readonly XmlReaderSettings closeInputSettings = new XmlReaderSettings { CloseInput = true };
      
      readonly char[] multipartNameSeparators = { '$', '/' };
      readonly bool xsiNil;
      readonly bool xsiType;
      readonly XmlQualifiedName tableName;
      readonly string rowName;

      IEnumerator<IDataRecord> enumerator;
      IDataRecord record;
      int fieldCount;
      int ordinal = -1;
      int multipartNameIndex;
      int multipartClose;
      string[] prevName;
      string[] name;
      string value;

      ReadState readState;
      XmlReadState xmlState;
      int depth;
      bool eof;
      List<XmlAttr> attribs = new List<XmlAttr>();
      int attrPos;
      bool attrRead;

      XmlNameTable _NameTable;
      XmlReaderSettings _Settings;

      public override int AttributeCount {
         get { return attribs.Count; }
      }

      public override string BaseURI {
         get { return null; }
      }

      public override int Depth {
         get { return depth; }
      }

      public override bool EOF {
         get { return eof; }
      }

      public override bool IsEmptyElement {
         get {
            return NodeType == XmlNodeType.Element
               && xmlState == XmlReadState.Field
               && multipartNameIndex == name.Length - 1
               && record.IsDBNull(ordinal);
         }
      }

      public override string LocalName {
         get {
            if (NodeType == XmlNodeType.Attribute)
               return attribs[attrPos - 1].LocalName;

            switch (xmlState) {
               case XmlReadState.Table:
                  return tableName.Name;

               case XmlReadState.Row:
                  return rowName;

               case XmlReadState.Field:
                  return name[multipartNameIndex];

               default:
                  return "";
            }
         }
      }

      public override XmlNameTable NameTable {
         get {
            if (_NameTable == null) {
               _NameTable = (Settings != null && Settings.NameTable != null) ?
                  Settings.NameTable : new NameTable();
            }
            return _NameTable;
         }
      }

      public override string NamespaceURI {
         get {
            if (NodeType == XmlNodeType.Attribute) {

               string prefix = attribs[attrPos - 1].Prefix;

               if (String.IsNullOrEmpty(prefix))
                  return "";

               switch (prefix) {
                  case "xmlns":
                     return "http://www.w3.org/2000/xmlns/";

                  case xsiPrefix:
                     return xsiNamespace;
               }

               throw new InvalidOperationException();

            } else if (NodeType == XmlNodeType.Element) {
               return tableName.Namespace;
            }

            return "";
         }
      }

      public override XmlNodeType NodeType {
         get {
            if (readState != ReadState.Interactive)
               return XmlNodeType.None;

            if (attrPos > 0) {
               if (attrRead)
                  return XmlNodeType.Text;
               return XmlNodeType.Attribute;
            }

            switch (xmlState) {
               case XmlReadState.Table:
               case XmlReadState.Row:
               case XmlReadState.Field:
                  return XmlNodeType.Element;

               case XmlReadState.Value:
                  return XmlNodeType.Text;

               case XmlReadState.EndField:
               case XmlReadState.EndRow:
               case XmlReadState.EndTable:
                  return XmlNodeType.EndElement;
            }

            throw new InvalidOperationException();
         }
      }

      public override string Prefix {
         get {
            if (NodeType == XmlNodeType.Attribute)
               return attribs[attrPos - 1].Prefix;

            return "";
         }
      }

      public override ReadState ReadState {
         get { return readState; }
      }

      public override XmlReaderSettings Settings {
         get { return _Settings; }
      }

      public override string Value {
         get {
            if (NodeType == XmlNodeType.Attribute
               || attrRead)
               return GetAttribute(attrPos - 1);

            if (xmlState != XmlReadState.Value)
               return "";

            return value;
         }
      }

#if !NET40
      public override bool HasValue {
         get {
            return (0L != (157084 & (1 << (int)NodeType)));
         }
      } 
#endif

      public XmlMappingReader(
         IEnumerator<IDataRecord> enumerator,
         XmlReaderSettings settings = null,
         XmlQualifiedName tableName = null,
         string rowName = null,
         bool xsiNil = false,
         bool xsiType = false) {

         if (enumerator == null) throw new ArgumentNullException("enumerator");

         this.enumerator = enumerator;
         this._Settings = settings ?? closeInputSettings;
         this.xsiNil = xsiNil;
         this.xsiType = xsiType;
         this.tableName = tableName ?? defaultTableName;
         this.rowName = rowName ?? defaultRowName;
      }

      public override void Close() {

         this.readState = ReadState.Closed;

         if (this.enumerator != null
            && this.Settings != null
            && this.Settings.CloseInput) {

            this.enumerator.Dispose();
         }

         this.enumerator = null;
      }

      public override string GetAttribute(int i) {

         if (this.attribs.Count - 1 < i)
            return null;

         return this.attribs[i].Value;
      }

      public override string GetAttribute(string name, string namespaceURI) {
         throw new NotImplementedException();
      }

      public override string GetAttribute(string name) {
         throw new NotImplementedException();
      }

      public override string LookupNamespace(string prefix) {
         return null;
      }

      public override bool MoveToAttribute(string name, string ns) {
         throw new NotImplementedException();
      }

      public override bool MoveToAttribute(string name) {
         throw new NotImplementedException();
      }

      public override bool MoveToElement() {
         return true;
      }

      public override bool MoveToFirstAttribute() {

         if (this.attribs.Count == 0) {
            this.attrPos = 0;
            return false;
         }

         this.attrPos = 1;
         return true;
      }

      public override bool MoveToNextAttribute() {

         if (this.attrPos <= 0)
            return MoveToFirstAttribute();

         this.attrRead = false;

         if (this.attrPos < this.attribs.Count) {
            this.attrPos++;
            return true;
         }

         this.attrPos = 0;
         return false;
      }

      public override bool Read() {

         if (this.EOF)
            return false;

         if (this.readState == ReadState.Initial) {

            this.readState = ReadState.Interactive;
            this.xmlState = XmlReadState.Table;

            if (!String.IsNullOrEmpty(this.tableName.Namespace))
               AddAttribute("", "xmlns", this.tableName.Namespace);

            if (this.xsiNil || this.xsiType)
               AddAttribute("xmlns", xsiPrefix, xsiNamespace);

            if (this.xsiType)
               AddAttribute("xmlns", xsPrefix, "http://www.w3.org/2001/XMLSchema");

            return true;
         }

         this.attribs.Clear();

         if (this.multipartClose > 0) {
            
            this.multipartClose--;
            this.multipartNameIndex--;
            this.depth--;

            if (this.multipartClose == 0) {
               this.xmlState = XmlReadState.Field;

               if (this.multipartNameIndex == this.name.Length - 1)
                  ReadValue();
            }

            return true;
         }

         switch (this.xmlState) {
            case XmlReadState.Table:

               if (MoveNextRow()) {
                  this.depth++;

               } else {
                  this.xmlState = XmlReadState.EndTable;
               }

               break;

            case XmlReadState.Row:

               if (this.fieldCount == 0) {
                  this.xmlState = XmlReadState.EndRow;
                  Debug.Assert(this.depth == 2);
               } else {

                  if (this.xsiNil) {
                     this.ordinal = 0;

                  } else {

                     for (int i = 0; i < this.fieldCount; i++) {
                        if (!this.record.IsDBNull(i)) {
                           this.ordinal = i;
                           break;
                        }
                     }
                  }

                  if (this.ordinal == -1) {
                     this.xmlState = XmlReadState.EndRow;
                     Debug.Assert(this.depth == 2);

                  } else {

                     this.xmlState = XmlReadState.Field;
                     this.depth++;

                     ReadField();

                     if (this.multipartNameIndex == this.name.Length - 1)
                        ReadValue();
                  }
               }

               break;

            case XmlReadState.Field:

               if (this.IsEmptyElement) {
                  this.xmlState = XmlReadState.EndField;
                  goto case XmlReadState.EndField;

               } else {

                  this.depth++;

                  if (this.multipartNameIndex < this.name.Length - 1) {
                     this.multipartNameIndex++;

                     if (this.multipartNameIndex == this.name.Length - 1) 
                        ReadValue();

                     break;
                  }

                  this.xmlState = XmlReadState.Value;
               }

               break;

            case XmlReadState.Value:

               this.depth--;
               this.xmlState = XmlReadState.EndField;

               break;

            case XmlReadState.EndField:

               int nextOrdinal = this.ordinal + 1;

               this.ordinal = -1;

               if (nextOrdinal > 0) {
                  if (this.xsiNil) {
                     this.ordinal = nextOrdinal;

                  } else {

                     for (int i = nextOrdinal; i < this.fieldCount; i++) {
                        if (!this.record.IsDBNull(i)) {
                           this.ordinal = i;
                           break;
                        }
                     }
                  } 
               }

               if (this.ordinal == -1
                  || this.ordinal + 1 > this.fieldCount) {
                  
                  this.depth--;

                  if (this.multipartNameIndex > 0) {
                     this.multipartNameIndex--;
                     break;
                  }

                  this.ordinal = -1;
                  this.xmlState = XmlReadState.EndRow;

               } else {

                  ReadField();

                  if (this.multipartNameIndex > 0) {
                     
                     // e.g.
                     // prevName: A$B$C
                     // name: A$D$E$F
                     // close C, B -> open D, E, F
                     
                     int i = 0;
                     for (; i < this.name.Length - 1; i++) {

                        if (i > this.prevName.Length - 1
                           || this.name[i] != this.prevName[i]) 
                           break;
                     }

                     this.multipartClose = this.prevName.Length - i - 1;

                     if (this.multipartClose > 0)
                        break;
                  }

                  this.xmlState = XmlReadState.Field;

                  if (this.multipartNameIndex == this.name.Length - 1)
                     ReadValue();
               }

               break;

            case XmlReadState.EndRow:

               if (!MoveNextRow()) {
                  this.xmlState = XmlReadState.EndTable;
                  this.depth--;
               }

               break;

            case XmlReadState.EndTable:

               this.eof = true;

               Debug.Assert(this.depth == 0);

               return false;
         }

         return true;
      }

      public override bool ReadAttributeValue() {

         if (this.attrPos > 0
            && this.attrPos <= this.attribs.Count
            && !this.attrRead) {

            this.attrRead = true;
            return true;
         }

         return false;
      }

      public override void ResolveEntity() {
         throw new NotImplementedException();
      }

      bool MoveNextRow() {

         bool moved = this.enumerator.MoveNext();

         if (moved) {
            this.record = this.enumerator.Current;
            this.xmlState = XmlReadState.Row;

            if (this.fieldCount == 0)
               this.fieldCount = this.record.FieldCount;
         }

         return moved;
      }

      void AddAttribute(string prefix, string localName, string value) {
         this.attribs.Add(new XmlAttr(prefix, localName, value));
      }

      void ReadField() {

         this.prevName = this.name;
         this.name = this.record.GetName(this.ordinal).Split(multipartNameSeparators, StringSplitOptions.RemoveEmptyEntries);

         if (this.name.Length == 0)
            this.name = new[] { defaultFieldName };
      }

      void ReadValue() {

         if (this.IsEmptyElement) {

            this.value = "";
            AddAttribute(xsiPrefix, "nil", "true");

            return;
         }

         string schemaType;

         this.value = SerializeValue(this.record[this.ordinal], out schemaType);

         if (this.xsiType && schemaType != null)
            AddAttribute("xsi", "type", xsPrefix + ":" + schemaType); 
      }

      static string SerializeValue(object value, out string schemaType) {

         schemaType = null;

         Type type = value.GetType();

         switch (Type.GetTypeCode(type)) {
            case TypeCode.Boolean:
               schemaType = "boolean";
               return XmlConvert.ToString((bool)value);

            case TypeCode.Byte:
               schemaType = "unsignedByte";
               return XmlConvert.ToString((byte)value);

            case TypeCode.DateTime:
               schemaType = "dateTime";
               return XmlConvert.ToString((DateTime)value, XmlDateTimeSerializationMode.RoundtripKind);

            case TypeCode.Decimal:
               schemaType = "decimal";
               return XmlConvert.ToString((decimal)value);

            case TypeCode.Double:
               schemaType = "double";
               return XmlConvert.ToString((double)value);

            case TypeCode.Int16:
               schemaType = "short";
               return XmlConvert.ToString((short)value);

            case TypeCode.Int32:
               schemaType = "int";
               return XmlConvert.ToString((int)value);

            case TypeCode.Int64:
               schemaType = "long";
               return XmlConvert.ToString((long)value);

            case TypeCode.SByte:
               schemaType = "byte";
               return XmlConvert.ToString((sbyte)value);

            case TypeCode.Single:
               schemaType = "float";
               return XmlConvert.ToString((float)value);

            case TypeCode.String:
               return (string)value;

            case TypeCode.UInt16:
               schemaType = "unsignedShort";
               return XmlConvert.ToString((ushort)value);

            case TypeCode.UInt32:
               schemaType = "unsignedInt";
               return XmlConvert.ToString((uint)value);

            case TypeCode.UInt64:
               schemaType = "unsignedLong";
               return XmlConvert.ToString((ulong)value);

            default:

               if (type == typeof(byte[])) {
                  schemaType = "base64Binary";
                  return Convert.ToBase64String((byte[])value);
               }

               break;
         }

         return Convert.ToString(value, CultureInfo.InvariantCulture);
      }

      #region Nested types

      enum XmlReadState {
         Table,
         Row,
         Field,
         Value,
         EndField,
         EndRow,
         EndTable,
      }

      sealed class XmlAttr {
         public readonly string Prefix;
         public readonly string LocalName;
         public readonly string Value;

         public XmlAttr(string prefix, string localName, string value) {
            this.Prefix = prefix;
            this.LocalName = localName;
            this.Value = value;
         }
      }

      #endregion
   }
}
