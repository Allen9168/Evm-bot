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
# 使用教程
从releases下载完整压缩包,或者用源码自编译,releases使用Github行动流程编译,非手动上传可放心使用
该软件只支持wss协议,不支持http协议,需要自备一个eth全节点
推荐(含邀请链接):



也可以自己搭建

本软件可以支持几乎所有evm系列
### 本软件可用范围:
#### 监控自定义调用
监控某某地址,这个地址一旦波动,就会调用私钥发送一笔自定义合约地址和自定义的调用数据,常见于很多通用场景,比如某些随机时间开场的一些存款,mint项目
#### 定时调用
设置好时间戳和gas和数据,定时对指定目标发送数据,常见于NFTmint,或者其他需要抢购的环境
#### 监控swap调用(已过期)
该项目针对uniswapv2,目前已过期

### 配置教程
下载打包的文件后,解压会有三个文件
```
├─Soha                               // 主程序
├─logs                              // 日志文件夹
├─Config                              // 配置文件
```
主程序通过控制台运行,建议在linux运行,如果在windows,需要使用CMD等运行软件
随后进入Config配置文件,大部分配置都是在里面操作
1:配置好wss
```
在Config/config.yaml
```
```
addres : "wss://ethereum-sepolia.core.chainstack.com/ced486101f05ba9f9bb0bd11112c826f"
```
这是示范这个wss://需要你自己去找,可以购买或者自己搭建,wss性能影响监控效果,如果要长期监控,公共免费的rpc几乎不可能支持

2:配置好地址和私钥
```
\Config\Account\
```
这个文件夹的所有.yaml文件都会被读取,默认文件是Main.yaml,只有一个,进去配置好你的私钥和地址即可,如果需要多个地址同时调用,直接复制多个Main1.yaml 2 3 4 5 这样的文件即可

3:配置好运行的项目
```
\Config\Project
```
写本教程的时候,现在基本只有Transaction.yaml,进去配置好即可,你要监控的地址,监控到了你要给哪个合约发送什么数据,上面开关打开,如果有多个合约,同样的复制粘贴多个项目文件即可
