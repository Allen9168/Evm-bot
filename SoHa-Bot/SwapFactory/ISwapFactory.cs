using System.Numerics;
using System.Threading.Tasks;

namespace Soha.SwapFactory
{
    public interface ISwapFactory
    {
        public event BaseSwap.AddLiquidity AddLiquidityEvent;
        public event BaseSwap.AddLiquidityETH AddLiquidityETHEvent;

        public string ETH { get; }
        public BigInteger Amount { get; }
        public string SwapFactoryAddress { get; }

        public Task SwapInvokeAsync(string tx, string from, string gasPrice, string maxFeePerGas, string maxPriorityFeePerGas, string value, string input);
        public string SwapExactETHForTokens(BigInteger amountOutMin, string[] path, string to, BigInteger deadline);
        public string SwapExactTokensForTokens(BigInteger amountIn, BigInteger amountOutMin, string[] path, string to, BigInteger deadline);
    }
}