# 企业级 CI/CD 完整流程详解

---

## 一、不是"一个CI" — 流水线是分层的

企业级实践中，流水线按**触发时机**分为多条，每条职责不同：

```
触发事件                    流水线名称              目的
─────────────────────────────────────────────────────────────
PR 创建/更新          →   PR Validation Pipeline    快速反馈，阻止坏代码合并
push to develop       →   CI Pipeline (Full)        完整构建+测试+推送镜像
push to release/*     →   Release Build Pipeline    构建RC版本，部署TEST
git tag v*.*.*        →   Release Deploy Pipeline   部署UAT/PROD
定时 (每天凌晨)        →   Nightly Pipeline          全量回归测试
手动触发              →   Hotfix Pipeline           紧急修复专用
```

---

## 二、各流水线详细定义

### 2.1 PR Validation Pipeline（最快，必须轻量）

```
触发: PR opened / PR synchronized (新 commit push)
目标: 5分钟内出结果，开发者等待中

┌──────────────────────────────────────────────────┐
│ Job 1: lint-and-compile          (并行)           │
│   - ESLint / Checkstyle / go vet                 │
│   - 编译检查 (fail fast)                          │
├──────────────────────────────────────────────────┤
│ Job 2: unit-test                 (并行)           │
│   - 只跑 Unit Test (不跑集成测试)                 │
│   - 生成覆盖率报告                                │
├──────────────────────────────────────────────────┤
│ Job 3: sonarqube-scan            (依赖 Job2)      │
│   - 上传覆盖率 + 代码扫描                         │
│   - Quality Gate 不通过 → PR 被 Block             │
├──────────────────────────────────────────────────┤
│ Job 4: security-scan             (并行)           │
│   - Dependency vulnerability (npm audit/snyk)    │
│   - Secret scanning (检测有没有硬编码密码)         │
│   - SAST 静态安全扫描                             │
├──────────────────────────────────────────────────┤
│ Job 5: docker-build-check        (并行)           │
│   - 只 build 不 push，验证 Dockerfile 不报错      │
└──────────────────────────────────────────────────┘

结果: 全部绿 → PR 可以被 Merge
      任一红 → PR 被 Block，通知开发者
```

---

### 2.2 CI Pipeline / Dev Deploy（merge to develop 触发）

```
触发: push to develop (PR merge 后自动触发)
目标: 构建制品，自动部署 DEV，为后续环境提供可部署的镜像

┌──────────────────────────────────────────────────┐
│ Job 1: full-test                                  │
│   - Unit Test                                    │
│   - Integration Test (起真实 DB/Redis 容器)       │
│   - E2E Test (可选，视速度而定)                   │
│   - 生成完整覆盖率报告                            │
├──────────────────────────────────────────────────┤
│ Job 2: sonar-full-scan           (依赖 Job1)      │
│   - 完整 SonarQube 分析                           │
├──────────────────────────────────────────────────┤
│ Job 3: build-and-push-image      (依赖 Job1)      │
│   - 多阶段 Docker Build                           │
│   - 镜像 tag: myapp:dev-{sha}                    │
│   - 推送到私有 Registry                           │
│   - 生成 SBOM (软件物料清单，企业合规要求)         │
├──────────────────────────────────────────────────┤
│ Job 4: deploy-dev                (依赖 Job3)      │
│   - 部署镜像到 DEV 服务器                         │
│   - 运行 Smoke Test (基础健康检查)                │
│   - 失败 → 自动回滚到上一个版本                   │
├──────────────────────────────────────────────────┤
│ Job 5: notify                    (依赖 Job4)      │
│   - 发送部署结果到 Slack / 企微                   │
│   - 更新 Jira/Linear 工单状态                     │
└──────────────────────────────────────────────────┘

产出物: 镜像 myapp:dev-a3f8c2d 已在 DEV 环境运行
```

---

### 2.3 Release Build Pipeline（切 release 分支触发）

```
触发: push to release/* 分支 (如 release/1.2.0)
目标: 构建 RC 版本，部署 TEST 环境供 QA 测试

注意: release 分支通常由开发 Lead 手动从 develop 切出
      git checkout -b release/1.2.0 develop

┌──────────────────────────────────────────────────┐
│ Job 1: full-test + regression                     │
│   - 完整测试套件                                  │
│   - 版本号注入 (从分支名提取 1.2.0)               │
├──────────────────────────────────────────────────┤
│ Job 2: build-release-image       (依赖 Job1)      │
│   - 镜像 tag: myapp:1.2.0-rc.1                   │
│   - RC 编号自动递增 (每次 push +1)                │
│   - 推送到 Registry                              │
├──────────────────────────────────────────────────┤
│ Job 3: deploy-test               (依赖 Job2)      │
│   - 部署到 TEST 环境                             │
│   - 运行自动化集成测试 / API 测试                 │
│   - 生成测试报告发给 QA                           │
├──────────────────────────────────────────────────┤
│ Job 4: create-release-notes      (依赖 Job2)      │
│   - 自动从 commit message 生成 CHANGELOG          │
│   - 创建 GitHub Draft Release                    │
└──────────────────────────────────────────────────┘

产出物: myapp:1.2.0-rc.1 在 TEST 环境，等待 QA 测试
        如果测试失败，在 release/1.2.0 上 fix，再 push → 自动触发 rc.2
```

---

### 2.4 Release Deploy Pipeline（打 tag 触发）

```
触发: push tag v*.*.* (如 v1.2.0)
      tag 由 Lead 在 release 分支通过后手动打，或 PR merge 到 main 时自动打

这条流水线不重新构建！复用已有镜像！

┌──────────────────────────────────────────────────┐
│ Job 1: promote-image                              │
│   - 从 Registry 拉取 myapp:1.2.0-rc.3 (最终RC)  │
│   - 重新打 tag 为 myapp:1.2.0                    │
│   - 推送正式 tag 镜像                             │
├──────────────────────────────────────────────────┤
│ Job 2: deploy-uat                (依赖 Job1)      │
│   - 部署 myapp:1.2.0 到 UAT                      │
│   - 运行冒烟测试                                  │
│   - 通知业务方"UAT 环境已就绪，请验收"            │
├──────────────────────────────────────────────────┤
│ Job 3: wait-for-uat-approval     (依赖 Job2)      │
│   - ⏸️ 暂停，等待人工审批                         │
│   - 在 GitHub/Jenkins 界面点击 Approve            │
│   - 设置超时时间 (如 72小时无人审批则失败)        │
├──────────────────────────────────────────────────┤
│ Job 4: deploy-prod               (依赖 Job3 审批) │
│   - 蓝绿部署 or 滚动更新                          │
│   - 每次更新 N% Pod，观察错误率                   │
│   - 健康检查通过 → 继续下一批                     │
│   - 错误率上升 → 自动回滚                         │
├──────────────────────────────────────────────────┤
│ Job 5: post-deploy               (依赖 Job4)      │
│   - 发布 GitHub Release (附 CHANGELOG)           │
│   - 更新监控 Dashboard 标记版本                   │
│   - 通知所有 Stakeholder                          │
└──────────────────────────────────────────────────┘
```

---

## 三、流水线依赖关系全景图

```
代码提交
    │
    ├─── PR ──────────────────────────────────────────────────────────┐
    │        lint → unit-test → sonar → security → docker-check      │
    │        全部通过 → PR 可合并                                      │
    └─────────────────────────────────────────────────────────────────┘
                                │ merge
                                ▼
                           develop 分支
                                │
                    push to develop ──→ CI Pipeline
                                         │
                                    ┌────┴─────────────────┐
                                    │                      │
                               full-test              security-scan
                                    │
                               build image (dev-{sha})
                                    │
                               deploy DEV ──→ smoke test
                                    │
                               ✓ DEV 稳定
                                    │
                    Lead 切 release/1.2.0 分支
                                    │
                    push to release/* ──→ Release Build Pipeline
                                              │
                                         full-test + regression
                                              │
                                         build (1.2.0-rc.1)
                                              │
                                         deploy TEST ──→ QA 测试
                                              │
                              ┌───── QA 发现 Bug ──────────────┐
                              │                                │
                    release 分支 fix → push            rc.2, rc.3...
                              │
                    QA 全部通过
                              │
                    PR: release/1.2.0 → main
                              │
                    merge to main ──→ 自动打 tag v1.2.0
                              │
                    push tag v1.2.0 ──→ Release Deploy Pipeline
                                              │
                                         promote image (1.2.0-rc.3 → 1.2.0)
                                              │
                                         deploy UAT ──→ 业务验收
                                              │
                                         ⏸ 人工审批
                                              │
                                         deploy PROD (蓝绿/滚动)
                                              │
                                         发布 Release Notes
```

---

## 四、Job 之间的依赖写法（GitHub Actions）

```yaml
# .github/workflows/release-deploy.yml

jobs:
  # Job1: 镜像晋级，不重新构建
  promote-image:
    runs-on: ubuntu-latest
    outputs:
      image-tag: ${{ steps.tag.outputs.version }}
    steps:
      - name: Extract version from tag
        id: tag
        run: echo "version=${GITHUB_REF#refs/tags/v}" >> $GITHUB_OUTPUT

      - name: Re-tag image (promote RC to release)
        run: |
          # 拉取最后一个 RC 版本
          RC_TAG=$(curl -s $REGISTRY_API/myapp/tags | jq -r '[.[] | select(startswith("${{ steps.tag.outputs.version }}-rc"))] | last')
          docker pull $REGISTRY/myapp:$RC_TAG
          docker tag  $REGISTRY/myapp:$RC_TAG $REGISTRY/myapp:${{ steps.tag.outputs.version }}
          docker push $REGISTRY/myapp:${{ steps.tag.outputs.version }}

  # Job2: 依赖 Job1
  deploy-uat:
    needs: promote-image # ← 依赖声明
    runs-on: ubuntu-latest
    environment: uat # ← 环境隔离，可配置不同 Secrets
    steps:
      - name: Deploy to UAT
        run: |
          helm upgrade --install myapp ./charts/myapp \
            --set image.tag=${{ needs.promote-image.outputs.image-tag }} \
            --namespace uat

  # Job3: 等人工审批 (environment protection rules)
  wait-approval:
    needs: deploy-uat
    runs-on: ubuntu-latest
    environment: prod-approval # ← 这个 environment 配置了 Required reviewers
    steps:
      - run: echo "Waiting for approval..."

  # Job4: 真正部署生产，依赖审批 Job
  deploy-prod:
    needs: wait-approval # ← 人审批后才执行
    runs-on: ubuntu-latest
    environment: production
    strategy:
      max-parallel: 1 # 滚动部署，串行
    steps:
      - name: Rolling Deploy to PROD
        run: |
          helm upgrade --install myapp ./charts/myapp \
            --set image.tag=${{ needs.promote-image.outputs.image-tag }} \
            --set rollout.strategy=rolling \
            --namespace production
```

---

## 五、多环境的 Secrets 和配置隔离

```
GitHub Repository Settings
├── Environments
│   ├── dev
│   │   ├── Variables: API_URL=https://dev.api.example.com
│   │   └── Secrets:   DB_PASSWORD=dev_xxx
│   ├── test
│   │   ├── Variables: API_URL=https://test.api.example.com
│   │   └── Secrets:   DB_PASSWORD=test_xxx
│   ├── uat
│   │   ├── Protection Rules: (无审批)
│   │   └── Secrets:   DB_PASSWORD=uat_xxx
│   ├── prod-approval          ← 只用于触发审批，不实际部署
│   │   └── Protection Rules: Required reviewers: [tech-lead, pm]
│   └── production
│       ├── Protection Rules: Required reviewers + 只允许 main 分支触发
│       └── Secrets:   DB_PASSWORD=prod_xxx (vault 管理)
```

---

## 六、企业级补充：镜像晋级策略

```
            开发                 测试                生产
            ──────               ──────              ──────
Registry:   myapp:dev-{sha}  →  myapp:1.2.0-rc.1  →  myapp:1.2.0
                                 myapp:1.2.0-rc.2
                                 myapp:1.2.0-rc.3

原则:
  ✓ 同一个 Dockerfile 构建产物贯穿所有环境（通过 tag 区分）
  ✓ 生产镜像 = 测试通过的 RC 镜像，只改 tag，不改内容
  ✓ 不同环境的差异通过 环境变量/ConfigMap 注入，而非重新构建
  ✗ 禁止: 为了部署到生产重新构建镜像（破坏一致性）
```

---

## 七、Hotfix 流程（企业必备）

```
生产出现紧急 Bug
      │
      ├── 从 main (或最新 tag) 切出 hotfix/1.2.1
      │
      ├── fix → push → 触发 PR Validation
      │
      ├── Code Review → Merge to hotfix/1.2.1
      │
      ├── 触发 Release Build Pipeline (部署到 TEST 快速验证)
      │
      ├── 验证通过 → 打 tag v1.2.1
      │
      ├── 触发 Release Deploy Pipeline → UAT → 审批 → PROD
      │
      └── hotfix 分支同步 merge 回 develop (防止下次发布又出现)
```

---

## 八、常见问题速查

| 问题                         | 答案                                                              |
| ---------------------------- | ----------------------------------------------------------------- |
| 为什么不直接在 main 上开发？ | main 要永远稳定可发布，直接开发风险太高                           |
| UAT 和 TEST 有什么区别？     | TEST 是技术测试，UAT 是业务方验收                                 |
| 镜像为什么不重新构建？       | 保证"测试通过的就是部署的"，一致性原则                            |
| Secrets 放哪里？             | GitHub Settings → Secrets，绝不放代码里                           |
| 出了问题怎么回滚？           | `git revert` 或直接部署上一个 tag 的镜像                          |
| RC 编号怎么管理？            | 每次 push 到 release 分支自动递增，CI 查询 Registry 已有 tag 数量 |
| 为什么要打 Tag？             | Tag 是不可变指针，精确标记发布点，branch 会移动但 tag 不会        |
