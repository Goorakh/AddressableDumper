using Newtonsoft.Json;
using System;

namespace AddressableDumper.ValueDumper.Serialization
{
    public class WriteOperation
    {
        public WriteOperationType Type { get; }

        readonly string _argument;

        public WriteOperation(WriteOperationType type, string argument)
        {
            Type = type;
            _argument = argument;

            switch (type)
            {
                case WriteOperationType.Comment:
                case WriteOperationType.Value:
                case WriteOperationType.ValueRaw:
                case WriteOperationType.PropertyName:
                    if (_argument == null)
                    {
                        throw new ArgumentNullException(nameof(argument), $"Operation {type} requires non-null argument");
                    }

                    break;
            }
        }

        public WriteOperation(WriteOperationType type) : this(type, null)
        {
        }

        public void Perform(JsonWriter writer)
        {
            switch (Type)
            {
                case WriteOperationType.Null:
                    writer.WriteNull();
                    break;
                case WriteOperationType.Comment:
                    writer.WriteComment(_argument);
                    break;
                case WriteOperationType.Value:
                    writer.WriteValue(_argument);
                    break;
                case WriteOperationType.ValueRaw:
                    writer.WriteRawValue(_argument);
                    break;
                case WriteOperationType.StartArray:
                    writer.WriteStartArray();
                    break;
                case WriteOperationType.EndArray:
                    writer.WriteEndArray();
                    break;
                case WriteOperationType.StartObject:
                    writer.WriteStartObject();
                    break;
                case WriteOperationType.EndObject:
                    writer.WriteEndObject();
                    break;
                case WriteOperationType.PropertyName:
                    writer.WritePropertyName(_argument);
                    break;
                default:
                    throw new NotImplementedException($"Operation type {Type} is not implemented");
            }
        }

        public WriteOperation Clone()
        {
            return new WriteOperation(Type, _argument);
        }

        public override string ToString()
        {
            return $"Type: {Type}, arg: '{_argument}'";
        }
    }
}
