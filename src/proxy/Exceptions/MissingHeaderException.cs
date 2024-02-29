namespace Proxy.Exceptions
{
    public class MissingHeaderException : Exception
    {
        public MissingHeaderException()
        {
        }

        public MissingHeaderException(string message)
            : base(message)
        {
        }

        public MissingHeaderException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
