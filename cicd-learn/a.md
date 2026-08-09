# CICD - First run pipeline

## 添加单元测试

### 创建测试项目
dotnet new xunit -n WebApplication1.Tests

### 加覆盖率收集
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage