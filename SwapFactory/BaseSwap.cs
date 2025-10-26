using Nethereum.ABI.Model;
using Nethereum.Contracts;
using Nethereum.Web3;
using System;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;

namespace Soha.SwapFactory
{
    public abstract class BaseSwap
    {
        public readonly string abi;
        public readonly string swapAddress;

        public delegate Task AddLiquidity(string tx, string from, BigInteger gasPrice, BigInteger? maxFeePerGas, BigInteger? maxPriorityFeePerGas, string tokenA, string tokenB, decimal amountADesired, decimal amountBDesired);
        public delegate Task AddLiquidityETH(string tx, string from, BigInteger gasPrice, BigInteger? maxFeePerGas, BigInteger? maxPriorityFeePerGas, decimal value, string token, decimal amountTokenDesired);

        public event AddLiquidity AddLiquidityEvent;
        public event AddLiquidityETH AddLiquidityETHEvent;

        public virtual BigInteger Amount => 0;
        protected readonly Web3 web3;
        protected readonly Contract contract;

        protected FunctionABI Liquidity;
        protected FunctionABI LiquidityETH;

        protected Function LiquidityFunction;
        protected Function LiquidityETHFunction;

        protected Function ExactETHForTokens;
        protected Function ExactTokensForTokens;

        public BaseSwap(string abi, string swapAddress)
        {
            this.abi = abi;
            this.swapAddress = swapAddress;
            web3 = new Web3();
            contract = web3.Eth.GetContract(abi, swapAddress);
        }
#nullable enable
        public virtual async Task SwapInvokeAsync(string tx, string from, string gasPrice, string? maxFeePerGas, string? maxPriorityFeePerGas, string value, string input)
        {
            string MethodID = input.Substring(2, 8);
            if (AddLiquidityEvent != null && MethodID.Equals(Liquidity.Sha3Signature, StringComparison.OrdinalIgnoreCase))
            {
                string[] data = DecodeSplit(input.Remove(0, 10));
                var _gasPrice = BigInteger.Parse($"0{gasPrice.Remove(0, 2)}", NumberStyles.HexNumber);

                BigInteger? _maxFeePerGas = null, _maxPriorityFeePerGas = null;
                if (!string.IsNullOrEmpty(maxFeePerGas))
                    _maxFeePerGas = BigInteger.Parse($"0{maxFeePerGas.Remove(0, 2)}", NumberStyles.HexNumber);
                if (!string.IsNullOrEmpty(maxPriorityFeePerGas))
                    _maxPriorityFeePerGas = BigInteger.Parse($"0{maxPriorityFeePerGas.Remove(0, 2)}", NumberStyles.HexNumber);
                await AddLiquidityEvent.Invoke(tx, from, _gasPrice, _maxFeePerGas, _maxPriorityFeePerGas, $"0x{data[0].Substring(24, 40)}", $"0x{data[1].Substring(24, 40)}", ((decimal)BigInteger.Parse(data[2], NumberStyles.HexNumber)), ((decimal)BigInteger.Parse(data[3], NumberStyles.HexNumber)));
            }
            else if (AddLiquidityETHEvent != null && MethodID.Equals(LiquidityETH.Sha3Signature, StringComparison.OrdinalIgnoreCase))
            {
                string[] data = DecodeSplit(input.Remove(0, 10));
                var _gasPrice = BigInteger.Parse($"0{gasPrice.Remove(0, 2)}", NumberStyles.HexNumber);
                BigInteger? _maxFeePerGas = null, _maxPriorityFeePerGas = null;
                if (!string.IsNullOrEmpty(maxFeePerGas))
                    _maxFeePerGas = BigInteger.Parse($"0{maxFeePerGas.Remove(0, 2)}", NumberStyles.HexNumber);
                if (!string.IsNullOrEmpty(maxPriorityFeePerGas))
                    _maxPriorityFeePerGas = BigInteger.Parse($"0{maxPriorityFeePerGas.Remove(0, 2)}", NumberStyles.HexNumber);
                await AddLiquidityETHEvent.Invoke(tx, from, _gasPrice, _maxFeePerGas, _maxPriorityFeePerGas, (decimal)BigInteger.Parse($"0{value.Remove(0, 2)}", NumberStyles.HexNumber), $"0x{data[0].Substring(24, 40)}", ((decimal)BigInteger.Parse(data[1], NumberStyles.HexNumber)));
            }
        }
#nullable disable
        private string[] DecodeSplit(string InPut)
        {
            int Length = InPut.Length / 64;
            string[] strArrayH = new string[Length];
            for (int i = 0; i < Length; i++)
            {
                strArrayH[i] = InPut.Substring(64 * i, 64);
            }
            return strArrayH;
        }
        public virtual string SwapExactETHForTokens(BigInteger amountOutMin, string[] path, string to, BigInteger deadline)
        {
            return ExactETHForTokens.GetData(amountOutMin, path, to, deadline);
        }
        public virtual string SwapExactTokensForTokens(BigInteger amountIn, BigInteger amountOutMin, string[] path, string to, BigInteger deadline)
        {
            return ExactTokensForTokens.GetData(amountIn, amountOutMin, path, to, deadline);
        }
    }
}