namespace AddressableDumper.ValueDumper.Serialization
{
    public interface IWriteOperationBuilderWriter
    {
        void Write(WriteOperationBuilder builder);

        void Flush();
    }
}
