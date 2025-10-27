# 项目结构图
```
Evm-Bot/
├─ Soha.csproj
├─ Program.cs                
├─ Robot.cs                 
├─ RobotUser.cs              
├─ Config/                 
│  ├─ Account/ 
│     └─Main.yaml
│  ├─ Project/    
│     ├─Invoke.yaml
│     ├─Invoke-Burning.yaml
│     ├─Swap.yaml
│     ├─Swap-Burning.yaml
│     └─Transaction.yaml
│  ├─ config.yaml
│  └─ logSetting.yaml
├─ Core/                    
│  ├─ Response/
│     └─Eth/
│       ├─BaseTransaction.cs
│       ├─TransactionResponse.cs
│       └─TransactionType.cs
│  ├─ AuthorizationHeader.cs
│  └─ RpcClient.cs
├─ Model/ 
│  ├─ Config/
│     ├─ApproveConfigModel.cs
│     ├─BurningConfigModel.cs
│     ├─DelayConfigModel.cs
│     ├─ExactConfigModel.cs
│     ├─InvokeConfigModel.cs
│     ├─LiquidityConfigModel.cs
│     ├─SellConfigModel.cs
│     ├─SwapConfigModel.cs
│     └─TransactionConfigModel.cs
│  ├─ Event/
│     └─NewHeads.cs
│  ├─ AccountConfig.cs
│  ├─ MonitorConfig.cs
│  └─ ProjectConfig.cs
├─ Service/ 
│  ├─ Project/
│     ├─BurningService.cs
│     ├─InvokeService.cs
│     ├─IProject.cs
│     ├─SwapService.cs
│     └─TransactionService.cs
│  ├─AccountManager.cs
│  ├─AccountService.cs
│  ├─ChainService.cs
│  ├─ProjectManager.cs
│  ├─TokenService.cs
│  └─TransactionsService.cs
├─ SwapFactory/ 
│  ├─ BinanceChain/
│     ├─ApeSwap.cs
│     ├─BabySwap.cs
│     └─PancakeSwap.cs
│  ├─ EthChain/
│     └─UniSwap.cs
│  ├─ MaticChain/
│     └─QuickSwap.cs
│  ├─ BaseSwap.cs
│  ├─ ISwapFactory.cs
│  └─ SwapFactory.cs
├─ Utils/ 
│  └─ TaskHelp.cs
```
