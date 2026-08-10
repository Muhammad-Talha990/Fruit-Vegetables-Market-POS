using System;

namespace FruitVegetableMarketPOS.Exceptions
{
    /// <summary>
    /// Custom exception for business rule violations in the POS system.
    /// </summary>
    public class BusinessException : Exception
    {
        public BusinessException(string message) : base(message)
        {
        }

        public BusinessException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
