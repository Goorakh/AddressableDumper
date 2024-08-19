using Newtonsoft.Json;

namespace AddressableDumper.ValueDumper.Serialization
{
    public class JsonWriteOperationBuilderWriter : IWriteOperationBuilderWriter
    {
        readonly JsonWriter _writer;

        public JsonWriteOperationBuilderWriter(JsonWriter writer)
        {
            _writer = writer;
        }

        public void Flush()
        {
            _writer.Flush();
        }

        public void Write(WriteOperationBuilder builder)
        {
            foreach (WriteOperation operation in builder)
            {
                operation.Perform(_writer);
            }
        }
    }
}
