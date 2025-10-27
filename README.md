# 项目结构图
```
Evm-Bot/                                      // 项目根
├─ Soha.csproj                                // [配置] .NET 项目文件/依赖与编译目标
├─ Program.cs                                  // [核心] 入口：加载配置、注册DI、启动 Robot
├─ Robot.cs                                    // [核心] 机器人编排：循环/调度/生命周期
├─ RobotUser.cs                                // [模型] 账号/用户参数（额度、地址等）
├─ Config/                                     // [配置] 所有外部化参数的目录
│  ├─ Account/                                 // [配置] 账户级配置
│  │  └─ Main.yaml                             // [配置] 主账户/地址/风控阈值等
│  ├─ Project/                                 // [配置] 策略/项目级配置
│  │  ├─ Invoke.yaml                           // [配置] 调用型策略参数（合约方法、参数）
│  │  ├─ Invoke-Burning.yaml                   // [配置] 调用+销毁策略参数
│  │  ├─ Swap.yaml                             // [配置] 交易/换币策略参数（滑点、路径）
│  │  ├─ Swap-Burning.yaml                     // [配置] 交易并销毁的复合策略
│  │  └─ Transaction.yaml                      // [配置] 交易通用参数（gas、deadline）
│  ├─ config.yaml                              // [配置] 全局：网络、RPC、默认gas、开关
│  └─ logSetting.yaml                          // [配置] 日志等级/输出格式
├─ Core/                                       // [核心] 链客户端/交易抽象/底层能力
│  ├─ Response/
│  │  └─ Eth/
│  │     ├─ BaseTransaction.cs                 // [抽象] 交易基类：to/data/value/gas 构造
│  │     ├─ TransactionResponse.cs             // [模型] 交易回执/状态/错误码
│  │     └─ TransactionType.cs                 // [模型] 交易类型枚举（Approve/Swap等）
│  ├─ AuthorizationHeader.cs                   // [工具] HTTP/RPC 鉴权头部拼装（如需）
│  └─ RpcClient.cs                             // [核心] 与节点交互：HTTP/WS 调用与重试
├─ Model/                                      // [模型] 领域与配置的强类型映射
│  ├─ Config/
│  │  ├─ ApproveConfigModel.cs                 // [模型] Approve 场景参数（额度/目标合约）
│  │  ├─ BurningConfigModel.cs                 // [模型] 销毁/回购参数
│  │  ├─ DelayConfigModel.cs                   // [模型] 延时/节流配置
│  │  ├─ ExactConfigModel.cs                   // [模型] 精确数量/最小成交量配置
│  │  ├─ InvokeConfigModel.cs                  // [模型] 合约方法调用参数
│  │  ├─ LiquidityConfigModel.cs               // [模型] 流动性添加/移除参数
│  │  ├─ SellConfigModel.cs                    // [模型] 卖出规则
│  │  ├─ SwapConfigModel.cs                    // [模型] 换币规则（路径/滑点/路由）
│  │  └─ TransactionConfigModel.cs             // [模型] 通用交易参数聚合
│  ├─ Event/
│  │  └─ NewHeads.cs                           // [模型] 新区块事件对象（订阅用）
│  ├─ AccountConfig.cs                         // [模型] 账户配置聚合（地址/私钥来源）
│  ├─ MonitorConfig.cs                         // [模型] 监控报警阈值
│  └─ ProjectConfig.cs                         // [模型] 项目级参数汇总（从 YAML 反序列化）
├─ Service/                                    // [服务] 业务逻辑与编排
│  ├─ Project/                                 // [服务] 各策略的实现
│  │  ├─ BurningService.cs                     // [服务] 销毁/回购策略执行
│  │  ├─ InvokeService.cs                      // [服务] 合约方法调用流程
│  │  ├─ IProject.cs                           // [抽象] 策略统一接口（Init/Run/Stop）
│  │  ├─ SwapService.cs                        // [服务] Swap 下单/回执/滑点处理
│  │  └─ TransactionService.cs                 // [服务] 通用交易构建与发送
│  ├─ AccountManager.cs                        // [服务] 多账户管理/切换/余额查询
│  ├─ AccountService.cs                        // [服务] 账户操作（approve、nonce管理）
│  ├─ ChainService.cs                          // [服务] 封装 Core.RpcClient 的高阶操作
│  ├─ ProjectManager.cs                        // [服务] 读取 ProjectConfig → 选择策略服务
│  ├─ TokenService.cs                          // [服务] Token 元数据/精度/授权
│  └─ TransactionsService.cs                   // [服务] 交易批处理/跟踪（与TransactionService配合）
├─ SwapFactory/                                // [适配器] DEX 路由工厂与各链实现
│  ├─ BinanceChain/
│  │  ├─ ApeSwap.cs                            // [适配器] BSC-ApeSwap 交易/报价适配
│  │  ├─ BabySwap.cs                           // [适配器] BSC-BabySwap 适配
│  │  └─ PancakeSwap.cs                        // [适配器] BSC-PancakeSwap 适配
│  ├─ EthChain/
│  │  └─ UniSwap.cs                            // [适配器] ETH-Uniswap 适配
│  ├─ MaticChain/
│  │  └─ QuickSwap.cs                          // [适配器] Polygon-QuickSwap 适配
│  ├─ BaseSwap.cs                              // [抽象] 适配器基类：编码路径、最小成交量等
│  ├─ ISwapFactory.cs                          // [抽象] 工厂接口：按网络/路由返回适配器
│  └─ SwapFactory.cs                           // [核心] 工厂实现：选择并实例化具体 DEX
├─ Utils/
│  └─ TaskHelp.cs                              // [工具] 任务/延时/重试等通用辅助
```
