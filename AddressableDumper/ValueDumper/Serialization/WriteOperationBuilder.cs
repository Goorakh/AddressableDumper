using System;
using System.Collections;
using System.Collections.Generic;

namespace AddressableDumper.ValueDumper.Serialization
{
    public class WriteOperationBuilder : IList<WriteOperation>, IWriteOperationBuilderWriter
    {
        readonly IWriteOperationBuilderWriter _output;

        readonly List<WriteOperation> _operations = [];

        public bool AutoFlush { get; set; } = true;

        public int AutoFlushCapacity { get; set; } = 64;

        public int Count => _operations.Count;

        public bool IsReadOnly => false;

        public WriteOperation this[int index]
        {
            get => _operations[index];
            set => _operations[index] = value;
        }

        public WriteOperationBuilder(IWriteOperationBuilderWriter output)
        {
            _output = output;
        }

        public void Add(WriteOperation operation)
        {
            if (operation is null)
                throw new ArgumentNullException(nameof(operation));

            _operations.Add(operation);

            tryAutoFlush();
        }

        public void AddRange(IEnumerable<WriteOperation> operations)
        {
            if (operations is null)
                throw new ArgumentNullException(nameof(operations));

            _operations.AddRange(operations);

            tryAutoFlush();
        }

        void tryAutoFlush()
        {
            if (!AutoFlush)
                return;

            if (_operations.Count >= AutoFlushCapacity)
            {
                if (_output != null)
                {
                    Flush();
                }
                else
                {
                    Log.Warning($"Large operation count of {_operations.Count}, add an output to flush contents");
                }
            }
        }

        void IWriteOperationBuilderWriter.Write(WriteOperationBuilder builder)
        {
            AddRange(builder);
        }

        public void Flush()
        {
            if (_output == null)
                throw new InvalidOperationException("Output writer must be defined to flush contents");

            if (_operations.Count > 0)
            {
                _output.Write(this);
                Clear();
            }
        }

        public void Clear()
        {
            _operations.Clear();
        }

        public void AddNull(string comment = null)
        {
            tryAddComment(comment);

            Add(new WriteOperation(WriteOperationType.Null));
        }

        void tryAddComment(string comment)
        {
            if (!string.IsNullOrEmpty(comment))
            {
                AddComment(comment);
            }
        }

        public void AddComment(string comment)
        {
            Add(new WriteOperation(WriteOperationType.Comment, comment));
        }

        public void AddValue(string valueString, string comment = null)
        {
            tryAddComment(comment);

            Add(new WriteOperation(WriteOperationType.Value, valueString));
        }

        public void AddValueRaw(string rawString, string comment = null)
        {
            tryAddComment(comment);

            Add(new WriteOperation(WriteOperationType.ValueRaw, rawString));
        }

        public void AddStartArray(string comment = null)
        {
            tryAddComment(comment);

            Add(new WriteOperation(WriteOperationType.StartArray));
        }

        public void AddEndArray(string comment = null)
        {
            tryAddComment(comment);

            Add(new WriteOperation(WriteOperationType.EndArray));
        }

        public void AddStartObject(string comment = null)
        {
            tryAddComment(comment);

            Add(new WriteOperation(WriteOperationType.StartObject));
        }

        public void AddEndObject(string comment = null)
        {
            tryAddComment(comment);

            Add(new WriteOperation(WriteOperationType.EndObject));
        }

        public void AddPropertyName(string name, string comment = null)
        {
            tryAddComment(comment);

            Add(new WriteOperation(WriteOperationType.PropertyName, name));
        }

        public int IndexOf(WriteOperation item)
        {
            return _operations.IndexOf(item);
        }

        public void Insert(int index, WriteOperation item)
        {
            _operations.Insert(index, item);
            tryAutoFlush();
        }

        public void RemoveAt(int index)
        {
            _operations.RemoveAt(index);
        }

        public bool Contains(WriteOperation item)
        {
            return _operations.Contains(item);
        }

        public void CopyTo(WriteOperation[] array, int arrayIndex)
        {
            _operations.CopyTo(array, arrayIndex);
        }

        public bool Remove(WriteOperation item)
        {
            return _operations.Remove(item);
        }

        public IEnumerator<WriteOperation> GetEnumerator()
        {
            return _operations.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
