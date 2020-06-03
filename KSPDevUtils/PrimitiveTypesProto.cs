// Kerbal Development tools.
// Author: igor.zavoychinskiy@gmail.com
// This software is distributed under Public domain license.

using System;
using System.ComponentModel;

namespace PartEquipment
{
    /// <summary>A proto for handling C# primitive types.</summary>
    public class PrimitiveTypesProto : AbstractOrdinaryValueTypeProto
    {
        /// <inheritdoc/>
        public override bool CanHandle(Type type) => type.IsPrimitive || type.IsEnum || type == typeof(string);

        /// <inheritdoc/>
        public override string SerializeToString(object value) => value.ToString();

        /// <inheritdoc/>
        public override object ParseFromString(string value, Type type)
        {
            try
            {
                return TypeDescriptor.GetConverter(type).ConvertFromString(value);
            }
            catch (Exception ex)
            {
                throw new ArgumentException(ex.Message);
            }
        }
    }
}
