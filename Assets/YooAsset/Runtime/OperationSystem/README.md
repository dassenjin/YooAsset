# OperationSystem 异步操作系统

## 模块概述

OperationSystem 是 YooAsset 资源管理系统的**异步操作调度核心**，负责管理所有异步操作的生命周期、调度执行和状态追踪。该模块提供了统一的异步操作抽象，支持协程、async/await、回调等多种异步编程模式。

### 核心职责

- 异步操作的统一抽象和生命周期管理
- 基于优先级的操作调度
- 时间切片执行（防止主线程阻塞）
- 多种异步编程模式支持
- 操作状态追踪和调试信息收集

---

## 设计目标

| 目标 | 说明 |
|------|------|
| **统一抽象** | 所有异步操作继承同一基类，接口一致 |
| **灵活调度** | 支持优先级排序、时间切片、帧预算控制 |
| **多模式支持** | 协程（IEnumerator）、Task（async/await）、回调 |
| **可调试性** | 完整的状态追踪、耗时统计、层级关系 |
| **线程安全** | 所有调度逻辑在主线程执行 |

---

## 架构概念

### 系统架构

```
┌─────────────────────────────────────────────────────────┐
│                    上层调用者                            │
│         (ResourceManager / FileSystem / 业务层)          │
└─────────────────────────┬───────────────────────────────┘
                          │ StartOperation()
┌─────────────────────────▼───────────────────────────────┐
│                   OperationSystem                        │
│                     (调度器)                             │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐      │
│  │  优先级队列  │  │  时间切片   │  │  回调通知   │      │
│  └─────────────┘  └─────────────┘  └─────────────┘      │
└─────────────────────────┬───────────────────────────────┘
                          │ UpdateOperation()
┌─────────────────────────▼───────────────────────────────┐
│                  AsyncOperationBase                      │
│                    (操作基类)                            │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐      │
│  │  状态机     │  │  子任务管理  │  │  异步模式   │      │
│  └─────────────┘  └─────────────┘  └─────────────┘      │
└─────────────────────────────────────────────────────────┘
```

### 核心组件

- **OperationSystem**: 静态调度器，管理所有操作的执行
- **OperationScheduler**: 包裹级调度器，维护操作队列并负责更新调度
- **AsyncOperationBase**: 异步操作基类，定义生命周期和状态
- **EOperationStatus**: 操作状态枚举

---

## 文件结构

```
OperationSystem/
├── EOperationStatus.cs        # 操作状态枚举
├── AsyncOperationBase.cs      # 异步操作基类
├── OperationScheduler.cs      # 包裹级调度器
├── OperationSystem.cs         # 异步操作调度器
```

---

## 枚举定义

### EOperationStatus（操作状态）

```csharp
public enum EOperationStatus
{
    None,        // 未开始
    Processing,  // 处理中
    Succeed,     // 已成功
    Failed       // 已失败
}
```

**状态转换：**

```
      StartOperation()              InternalUpdate()
None ─────────────────► Processing ─────────────────┬──► Succeed
                                                    │
                                                    └──► Failed
```

---

## 核心类说明

### AsyncOperationBase（异步操作基类）

所有异步操作的抽象基类，实现了 `IEnumerator` 和 `IComparable<AsyncOperationBase>` 接口。

#### 公共属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `Priority` | `uint` | 任务优先级（值越大越优先） |
| `Status` | `EOperationStatus` | 当前状态 |
| `Error` | `string` | 错误信息（失败时） |
| `Progress` | `float` | 处理进度（0-1） |
| `IsDone` | `bool` | 是否已完成（Succeed 或 Failed） |
| `Task` | `Task` | 用于 async/await |
| `BeginTime` | `string` | 开始时间（调试用） |
| `ProcessTime` | `long` | 处理耗时毫秒（调试用） |

> 说明：`AsyncOperationBase` 本身不保存包裹名称；包裹名称由 `OperationSystem.StartOperation(packageName, operation)` 传入，并由 `OperationScheduler` 维护。

#### 内部协作：时间切片（`IsBusy`）

为配合 `OperationSystem.MaxTimeSlice` 的时间切片预算，`AsyncOperationBase` 提供了内部属性 `IsBusy`（`internal`）用于任务在 `InternalUpdate()` 内主动让出本帧预算。

- 推荐用法：在 `InternalUpdate()` 内部（或内部子步骤）在执行重逻辑前判断 `IsBusy`，若繁忙则 `return`，把工作拆到下一帧继续执行。
- 同步等待特殊处理：当调用了 `WaitForAsyncComplete()` 进入同步等待阶段时，`IsBusy` 会强制返回 `false`，避免因时间切片判断导致同步等待无法推进。
- 注意：`WaitForAsyncComplete()` 会阻塞主线程，应谨慎使用；同步等待阶段不受时间切片保护，可能带来卡顿。

#### 公共事件

```csharp
/// <summary>
/// 完成事件（支持后注册立即触发）
/// </summary>
public event Action<AsyncOperationBase> Completed;
```

#### 公共方法

```csharp
/// <summary>
/// 同步等待异步操作完成
/// </summary>
public void WaitForAsyncComplete();
```

#### 内部抽象方法（子类实现）

| 方法 | 说明 |
|------|------|
| `InternalStart()` | 操作开始时调用 |
| `InternalUpdate()` | 每帧更新时调用 |
| `InternalAbort()` | 操作中止时调用（可选） |
| `InternalWaitForAsyncComplete()` | 同步等待时调用（可选） |
| `InternalGetDesc()` | 获取操作描述（可选） |

#### 子任务管理

```csharp
// 添加/移除子任务（内部使用）
internal void AddChildOperation(AsyncOperationBase child);
internal void RemoveChildOperation(AsyncOperationBase child);
```

**调用约束（重要）：**
- 仅允许在 Unity 主线程调用（与 `OperationSystem.Update()` 的调度线程一致）。
- 不要在 `InternalUpdate()` 正在遍历/处理中途频繁增删子任务；推荐在任务启动阶段完成子任务挂接，或在确保无并发修改风险的安全点调整。
- `AbortOperation()` 会递归中止子任务，子任务的生命周期由父任务统一管理；避免在 `Completed` 回调里再去修改子任务关系，防止时序混乱。

---

### OperationSystem（调度器）

静态类，负责异步操作的调度和管理。

#### 配置属性

```csharp
/// <summary>
/// 每帧最大执行时间（毫秒）
/// 默认值：long.MaxValue（无限制）
/// </summary>
public static long MaxTimeSlice { set; get; }

/// <summary>
/// 当前帧是否已超时
/// </summary>
public static bool IsBusy { get; }
```

#### 核心方法

```csharp
/// <summary>
/// 初始化异步操作系统
/// </summary>
public static void Initialize();

/// <summary>
/// 每帧更新（由 YooAssets 驱动）
/// </summary>
public static void Update();

/// <summary>
/// 销毁所有操作
/// </summary>
public static void DestroyAll();

/// <summary>
/// 清理指定包裹的所有操作
/// </summary>
public static void ClearPackageOperation(string packageName);

/// <summary>
/// 启动异步操作
/// </summary>
public static void StartOperation(string packageName, AsyncOperationBase operation);
```

#### 包裹调度说明

- `packageName` 不允许为空（`null` / `""`），否则会抛出异常。
- 若需要使用全局调度器，请传入 `OperationSystem.GLOBAL_SCHEDULER_NAME`（`Initialize()` 时自动创建）。
- `packageName` 为非全局调度器名称时，必须先通过 `YooAssets.CreatePackage(packageName)` 创建包裹（内部会注册对应 `OperationScheduler`），否则会抛出异常。

#### 回调监听

OperationSystem **当前未提供**全局任务开始/结束回调的注册接口。

如需监听任务结束（推荐），请直接订阅具体任务的 `Completed` 事件：

```csharp
var operation = package.LoadAssetAsync<GameObject>(location);
operation.Completed += op =>
{
    // TODO : 根据 op.Status 判断成功/失败
};
```

---

## 异步编程模式

### 1. 协程模式（IEnumerator）

```csharp
IEnumerator LoadAsset()
{
    var operation = package.LoadAssetAsync<GameObject>("Assets/Prefab.prefab");
    yield return operation;

    if (operation.Status == EOperationStatus.Succeed)
    {
        GameObject prefab = operation.AssetObject as GameObject;
    }
}
```

### 2. Task 模式（async/await）

```csharp
async Task LoadAssetAsync()
{
    var operation = package.LoadAssetAsync<GameObject>("Assets/Prefab.prefab");
    await operation.Task;

    if (operation.Status == EOperationStatus.Succeed)
    {
        GameObject prefab = operation.AssetObject as GameObject;
    }
}
```

### 3. 回调模式（Completed 事件）

```csharp
void LoadAsset()
{
    var operation = package.LoadAssetAsync<GameObject>("Assets/Prefab.prefab");
    operation.Completed += OnLoadCompleted;
}

void OnLoadCompleted(AsyncOperationBase op)
{
    var operation = op as AssetHandle;
    if (operation.Status == EOperationStatus.Succeed)
    {
        GameObject prefab = operation.AssetObject as GameObject;
    }
}
```

### 4. 同步等待模式

```csharp
void LoadAssetSync()
{
    var operation = package.LoadAssetAsync<GameObject>("Assets/Prefab.prefab");
    operation.WaitForAsyncComplete();  // 阻塞等待完成

    if (operation.Status == EOperationStatus.Succeed)
    {
        GameObject prefab = operation.AssetObject as GameObject;
    }
}
```

---

## 调度机制

### 优先级调度

操作按 `Priority` 属性降序排列，优先级高的操作先执行。

```csharp
var operation = package.LoadAssetAsync<GameObject>(location);
operation.Priority = 100;  // 设置高优先级
```

**排序规则：**
- 新操作添加时：若新增队列存在非零优先级，则触发排序
- 运行中修改 `Priority`：调度器会在每帧 `Update()` 的排序阶段检测 `IsDirty` 并触发重排；若在某个操作的 `InternalUpdate()` 内修改（本帧排序已完成），则新的优先级会延后一帧生效（可能与预期不符）
- 若期望本帧生效：请尽量在任务入队前或本帧调度器 `Update()` 开始前设置 `Priority`，避免在 `InternalUpdate()` 内临时调整
- 排序使用 `List.Sort()` 进行原地排序；频繁修改优先级会带来额外排序开销，建议按需使用

### 时间切片

通过 `MaxTimeSlice` 控制每帧最大执行时间，防止主线程阻塞。

```csharp
// 设置每帧最多执行 8 毫秒
OperationSystem.MaxTimeSlice = 8;
```

**操作侧协作建议：**
- 在操作的 `InternalUpdate()` 中使用 `IsBusy`（`AsyncOperationBase` 的内部属性）进行“自愿让出”，将重任务拆分到多帧执行。
- 在同步等待（`WaitForAsyncComplete()`）阶段，`IsBusy` 会强制返回 `false`，以保证同步等待推进；此时需要自行评估卡顿风险。

**执行流程：**

```
每帧 Update()
    │
    ├── 记录帧开始时间 _frameTime
    │
    └── 遍历操作队列
            │
            ├── 检查 IsBusy（是否超时）
            │       │
            │       └── 超时则中断本帧
            │
            └── 执行 operation.UpdateOperation()
```

### 操作生命周期

```
┌─────────────────────────────────────────────────────────────────┐
│                        操作生命周期                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   1. 创建操作                                                    │
│      └── Status = None                                          │
│                                                                 │
│   2. StartOperation()                                           │
│      ├── Status = Processing                                    │
│      ├── DebugBeginRecording()                                  │
│      ├── InternalStart()                                        │
│      └── 添加到 _newList                                         │
│                                                                 │
│   3. Update() - 每帧调度                                         │
│      ├── 移除已完成操作                                          │
│      ├── 合并 _newList 到 _operations                            │
│      ├── 按优先级排序（如需要）                                   │
│      └── 遍历执行 UpdateOperation()                              │
│                                                                 │
│   4. UpdateOperation()                                          │
│      ├── DebugUpdateRecording()                                 │
│      ├── InternalUpdate()                                       │
│      └── 检查 IsDone                                             │
│             │                                                   │
│             └── 完成时：                                         │
│                  ├── IsFinish = true                            │
│                  ├── Progress = 1f                              │
│                  ├── DebugEndRecording()                        │
│                  ├── 触发 Completed 回调                         │
│                  └── 设置 TaskCompletionSource                   │
│                                                                 │
│   5. 下一帧移除完成的操作                                         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 调试支持

### 调试信息结构

```csharp
[Serializable]
internal struct DebugOperationInfo
{
    public string OperationName;   // 任务名称
    public string OperationDesc;   // 任务说明
    public uint Priority;          // 优先级
    public float Progress;         // 任务进度
    public string BeginTime;       // 任务开始的时间
    public long ProcessTime;       // 处理耗时（单位：毫秒）
    public string Status;          // 任务状态
    public List<DebugOperationInfo> Childs;  // 子任务列表（注意：JsonUtility 序列化深度限制）
}
```

> 说明：该结构体真实定义位于 `Runtime/DiagnosticSystem/DebugOperationInfo.cs`，这里仅展示关键字段以便理解。

### 获取调试信息

```csharp
// 获取指定包裹的所有操作信息（内部调试接口）
// packageName 不允许为空；全局调度器请使用 OperationSystem.GLOBAL_SCHEDULER_NAME
// 非全局包裹需先 YooAssets.CreatePackage(packageName)
var infos = OperationSystem.GetDebugOperationInfos(OperationSystem.GLOBAL_SCHEDULER_NAME);

foreach (var info in infos)
{
    Debug.Log($"{info.OperationName}: {info.Status}, {info.ProcessTime}ms");
}
```

### 耗时统计

在 DEBUG 模式下自动统计操作耗时：

```csharp
// 操作完成后可获取耗时
Debug.Log($"开始时间: {operation.BeginTime}");
Debug.Log($"处理耗时: {operation.ProcessTime}ms");
```

---

## 设计模式

### 模板方法模式

`AsyncOperationBase` 定义算法骨架，子类实现具体步骤：

```
AsyncOperationBase
    │
    ├── StartOperation()     ──► InternalStart()    [子类实现]
    ├── UpdateOperation()    ──► InternalUpdate()   [子类实现]
    ├── AbortOperation()     ──► InternalAbort()    [子类实现]
    └── WaitForAsyncComplete() ──► InternalWaitForAsyncComplete() [子类实现]
```

### 状态机模式

操作状态由 `EOperationStatus` 管理：

```
┌──────┐  StartOperation()  ┌────────────┐  UpdateOperation()  ┌─────────┐
│ None │ ─────────────────► │ Processing │ ──────────────────► │ Succeed │
└──────┘                    └────────────┘                     └─────────┘
                                   │
                                   │ UpdateOperation() / AbortOperation()
                                   ▼
                            ┌──────────┐
                            │  Failed  │
                            └──────────┘
```

### 组合模式

通过内部子任务列表支持父子操作关系：

```
ParentOperation
    ├── ChildOperation1
    ├── ChildOperation2
    └── ChildOperation3
            └── GrandChildOperation
```

---

## 类继承关系

```
IEnumerator + IComparable<AsyncOperationBase>
              │
              ▼
    AsyncOperationBase (抽象基类)
              │
              └── [YooAsset 内部操作]
                      │
                      ├── InitializationOperation
                      ├── LoadAssetOperation
                      ├── LoadSceneOperation
                      ├── DownloadOperation
                      └── ...
```

---

## 注意事项

1. **主线程执行**：所有操作的调度和更新都在 Unity 主线程执行
2. **时间切片**：设置合理的 `MaxTimeSlice` 避免卡顿（建议 8-16ms）
3. **同步等待**：`WaitForAsyncComplete()` 会阻塞主线程，谨慎使用
4. **子任务中止**：父操作中止时会自动中止所有子操作
5. **回调异常**：`Completed` 回调中的异常会被捕获并记录，不会中断系统
6. **编辑器重置**：编辑器中使用 `RuntimeInitializeOnLoadMethod` 自动重置状态
7. **循环保护**：在 `InternalWaitForAsyncComplete()` 中建议使用 `RunBatchExecution()`（默认 1000 次）限制单次推进次数，避免陷入无限循环或长时间占用主线程
