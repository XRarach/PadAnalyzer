﻿using Dia2Lib;
using System.IO;

namespace GenerateUserTypesFromPdb.UserTypes
{
    class EnumUserType : UserType
    {
        public EnumUserType(Symbol symbol, string moduleName, string nameSpace)
            : base(symbol, new XmlType() { Name = symbol.Name }, moduleName, nameSpace)
        {
        }

        public override void WriteCode(IndentedWriter output, TextWriter error, UserTypeFactory factory, UserTypeGenerationFlags options, int indentation = 0)
        {
            if (DeclaredInType == null && !string.IsNullOrEmpty(Namespace))
            {
                output.WriteLine(indentation, "namespace {0}", Namespace);
                output.WriteLine(indentation++, "{{");
            }

            if (options.HasFlag(UserTypeGenerationFlags.GenerateFieldTypeInfoComment))
                output.WriteLine(indentation, "// {0} (original name: {1})", ClassName, Symbol.Name);

            if (Symbol.Size != 0)
                output.WriteLine(indentation, @"public enum {0} : {1}", ClassName, GetEnumType());
            else
                output.WriteLine(indentation, @"public enum {0}", ClassName);
            output.WriteLine(indentation++, @"{{");

            foreach (var enumValue in Symbol.GetEnumValues())
            {
                output.WriteLine(indentation, "{0} = {1},", enumValue.Item1, enumValue.Item2);
            }

            // Class end
            output.WriteLine(--indentation, @"}}");

            if (DeclaredInType == null && !string.IsNullOrEmpty(Namespace))
            {
                output.WriteLine(--indentation, "}}");
            }
        }

        private string GetEnumType()
        {
            switch (Symbol.BasicType)
            {
                case BasicType.Int:
                case BasicType.Long:
                    switch (Symbol.Size)
                    {
                        case 8:
                            return "long";
                        case 4:
                            return "int";
                        case 2:
                            return "short";
                        case 1:
                            return "sbyte";
                        case 0:
                            return string.Empty;
                        default:
                            break;
                    }
                    break;

                case BasicType.UInt:
                case BasicType.ULong:
                    switch (Symbol.Size)
                    {
                        case 8:
                            return "ulong";
                        case 4:
                            return "uint";
                        case 2:
                            return "ushort";
                        case 1:
                            return "byte";
                        case 0:
                            return string.Empty;
                        default:
                            break;
                    }
                    break;

                default:
                    break;
            }

            throw new InvalidDataException("Unknown enum type.");
        }

        /// <summary>
        /// Full Class Name.
        /// Handle special logic for enums embedded in template types.
        /// </summary>
        public override string FullClassName
        {
            get
            {
                if (DeclaredInType as TemplateUserType != null)
                {
                    // Enum cannot be instantiated in generic type.
                    // We must choose template specialization - first on the list.
                    //
                    TemplateUserType declaredInTemplateUserType = (DeclaredInType as TemplateUserType);

                    string declaredInSpecializedType = declaredInTemplateUserType.GetSpecializedTypeDefinedInstance();

                    if (declaredInSpecializedType.Contains("SequencedObject"))
                    {

                    }

                    return string.Format("{0}.{1}", declaredInSpecializedType, ClassName);
                }
                else
                {
                    return base.FullClassName;
                }
            }
        }
    }
}