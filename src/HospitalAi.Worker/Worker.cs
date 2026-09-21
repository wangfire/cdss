// 该文件为死代码清理留存的占位文件，原 Phase 1 Task 7 之前的脚手架 Worker BackgroundService 已删除。
// 真实的异步消费者为 RabbitMQ 驱动的 CodingTaskCreatedConsumer，由 Program.cs 中的 AddCodingTaskConsumer 注册。
// 请勿在此重新引入 BackgroundService；如需 Worker 进程级心跳，请通过 MassTransit 的消费者活动或 Serilog 周期日志实现。
namespace HospitalAi.Worker;
