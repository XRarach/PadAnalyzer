﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenerateUserTypesFromPdb.UserTypes
{
    class GlobalsUserType : UserType
    {
        public GlobalsUserType(Symbol symbol, XmlType type, string moduleName, string nameSpace)
            : base(symbol, type, moduleName, nameSpace)
        {
        }

        public override string ClassName
        {
            get
            {
                return XmlType.Name;
            }
        }

        internal override IEnumerable<UserTypeField> ExtractFields(UserTypeFactory factory, UserTypeGenerationFlags options)
        {
            var fields = Symbol.Fields.OrderBy(s => s.Name).ToArray();
            bool useThisClass = options.HasFlag(UserTypeGenerationFlags.UseClassFieldsFromDiaSymbolProvider);
            string previousName = "";

            foreach (var field in fields)
            {
                if (string.IsNullOrEmpty(field.Type.Name))
                    continue;

                if (IsFieldFiltered(field) || field.Name == previousName)
                    continue;

                // Skip fields that have same name as the type
                UserType userType;
                factory.TryGetUserType(field.Type.Module, field.Type.Name, out userType);

                if (userType == null)
                    continue;

                // Skip fields that are actual values of enum values
                if (field.Type.Tag == Dia2Lib.SymTagEnum.SymTagEnum && field.Type.GetEnumValues().Where(t => t.Item1 == field.Name).Any())
                    continue;

                var userField = ExtractField(field, factory, options, forceIsStatic: true);

                userField.FieldName = userField.FieldName.Replace("?", "_").Replace("$", "_").Replace("@", "_").Replace(":", "_").Replace(" ", "_").Replace("<", "_").Replace(">", "_").Replace("*", "_").Replace(",", "_");
                userField.PropertyName = userField.PropertyName.Replace("?", "_").Replace("$", "_").Replace("@", "_").Replace(":", "_").Replace(" ", "_").Replace("<", "_").Replace(">", "_").Replace("*", "_").Replace(",", "_");

                yield return userField;
                previousName = field.Name;
            }

            foreach (var field in GetAutoGeneratedFields(false, useThisClass))
                    yield return field;
        }


        protected override UserTypeTree GetBaseTypeString(TextWriter error, Symbol type, UserTypeFactory factory)
        {
            return new UserTypeStaticClass();
        }

        protected override IEnumerable<UserTypeConstructor> GenerateConstructors()
        {
            yield return new UserTypeConstructor()
            {
                ContainsFieldDefinitions = true,
                Static = true,
            };
        }
    }
}