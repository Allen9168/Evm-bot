# 项目结构图
```
Evm-Bot/
├─ Soha.csproj
├─ Program.cs                // 控制台应用入口：DI容器、配置加载、启动机器人
├─ Robot.cs                  // 机器人主体（编排器）：策略循环、任务调度、生命周期管理
├─ RobotUser.cs              // 用户/账号配置模型（昵称、地址、风控限额等）
├─ Config/                   // 配置与样例密钥（请勿提交真实私钥）
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
├─ Core/                     // “核心底座”：链客户端、钱包、交易签名、订阅、工具
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
```
│  ├─ BaseSwap.cs
│  ├─ ISwapFactory.cs
│  └─ SwapFactory.cs
├─ Utils/ 
│  └─ TaskHelp.cs
