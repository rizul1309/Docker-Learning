# AWS Infrastructure & Observability — SDE-3 Hands-On Guide

> A practical, end-to-end reference for an SDE-3 who already has access to a company AWS account.
> Open each service in the AWS Console / Grafana as you read. The goal is to *experience* the infra, not just read about it.

---

## Table of Contents

1. [Mindset Shift: SDE-3 Ownership](#1-mindset-shift-sde-3-ownership)
2. [AWS Networking Deep Dive (VPC, Subnets, Security Groups)](#2-aws-networking-deep-dive)
3. [Compute — EC2, Auto Scaling Groups, Launch Templates](#3-compute--ec2-auto-scaling-groups-launch-templates)
4. [Containers — ECS (Elastic Container Service) End-to-End](#4-containers--ecs-elastic-container-service-end-to-end)
5. [Container Orchestration — EKS (Elastic Kubernetes Service)](#5-container-orchestration--eks)
6. [Load Balancing — ALB, NLB, Target Groups](#6-load-balancing--alb-nlb-target-groups)
7. [CI/CD Pipeline — CodePipeline, CodeBuild, CodeDeploy, ECR](#7-cicd-pipeline)
8. [Infrastructure as Code — CloudFormation & Terraform](#8-infrastructure-as-code)
9. [Storage — S3 Deep Dive (Caching, Lifecycle, Replication)](#9-storage--s3-deep-dive)
10. [Caching — ElastiCache (Redis/Memcached), CloudFront](#10-caching--elasticache-cloudfront)
11. [Databases — RDS, DynamoDB, Aurora](#11-databases--rds-dynamodb-aurora)
12. [Messaging & Async — SQS, SNS, EventBridge](#12-messaging--async--sqs-sns-eventbridge)
13. [Secrets & Configuration — Secrets Manager, Parameter Store, KMS](#13-secrets--configuration)
14. [IAM Deep Dive — Roles, Policies, Cross-Account, Least Privilege](#14-iam-deep-dive)
15. [Logging — CloudWatch Logs, Log Insights, Structured Logging](#15-logging)
16. [Metrics & Alarms — CloudWatch Metrics, Custom Metrics, Alarms](#16-metrics--alarms)
17. [Distributed Tracing — X-Ray, OpenTelemetry](#17-distributed-tracing)
18. [Grafana — Dashboards, Data Sources, Alerts, Loki, Tempo](#18-grafana-deep-dive)
19. [Incident Response & On-Call Ownership](#19-incident-response--on-call)
20. [Cost Optimization & FinOps](#20-cost-optimization--finops)
21. [Security & Compliance](#21-security--compliance)
22. [Performance Engineering](#22-performance-engineering)
23. [Disaster Recovery & High Availability](#23-disaster-recovery--high-availability)
24. [Real-World Scenarios & Runbooks](#24-real-world-scenarios--runbooks)

---

## 1. Mindset Shift: SDE-3 Ownership

At SDE-3 you are not just writing features. You **own** systems end-to-end:

- You design the architecture and defend trade-offs in design reviews.
- You own deployment pipelines, rollback strategies, and release safety.
- You own observability: if it's not monitored, it's not shipped.
- You own cost: every resource you provision has a dollar amount.
- You own security posture of your services.
- You mentor SDE-1/2s on all of the above.

**Action:** Open your company's AWS Console right now. Go to **Billing → Cost Explorer**. See what your team spends. This is your money now.

---

## 2. AWS Networking Deep Dive

> This section is explained using YOUR company's actual VPC as an example.
> Everything below maps directly to what you see in your AWS Console.

---

### 2.1 The Big Picture — What Is a VPC?

Imagine you rented an entire office building. The building is YOUR space — no other company can walk in.
That building is your **VPC (Virtual Private Cloud)**. It's your private, isolated network inside AWS.

Your company's VPC:
```
VPC Name:  ITAM-NP-VPC-AP-SOUTHEAST-2
CIDR:      10.203.104.0/22
Region:    ap-southeast-2 (Sydney, Australia)
```

**What does `10.203.104.0/22` mean?**

Think of CIDR as the "size of your building." The `/22` part tells you how many IP addresses (rooms) you have:

```
/22 = 2^(32-22) = 2^10 = 1,024 IP addresses

Your IP range: 10.203.104.0 → 10.203.107.255
That's 1,024 addresses to split across all your subnets.

For comparison:
  /16 = 65,536 IPs  (huge building — entire campus)
  /22 = 1,024 IPs   (medium building — your VPC)
  /24 = 256 IPs     (one floor — a single subnet)
  /26 = 64 IPs      (one room — a small subnet)
  /19 = 8,192 IPs   (a large wing)
```

**Real-world analogy:**
```
Your VPC (10.203.104.0/22) = An office building with 1,024 rooms
├── Floor 1 (subnet A) gets rooms 0-255
├── Floor 2 (subnet B) gets rooms 256-511
├── Floor 3 (subnet C) gets rooms 512-767
└── Floor 4 (subnet D) gets rooms 768-1023

Each "room" is an IP address that a server/container/database can use.
```

---

### 2.2 Subnets — Your VPC's Floors

Your VPC is divided into **9 subnets** spread across **3 Availability Zones (AZs)**.

**What is an Availability Zone?**
An AZ is a physically separate data center within a region. If one data center catches fire or loses power, the others keep running. Sydney (ap-southeast-2) has 3 AZs:

```
ap-southeast-2a = Data Center A in Sydney
ap-southeast-2b = Data Center B in Sydney (different building, different power grid)
ap-southeast-2c = Data Center C in Sydney (yet another separate facility)
```

**Your actual subnet layout (from the screenshot):**

```
VPC: ITAM-NP-VPC-AP-SOUTHEAST-2 (10.203.104.0/22)
│
├── ap-southeast-2a (Data Center A)
│   ├── 🟢 ITAM-NP-PUBLICSUBNET-NONPROD-A     (10.203.104.0/26)  ← PUBLIC
│   │       64 IPs. Faces the internet. Your ALB sits here.
│   │
│   ├── 🔵 nonprod-shared-internal-subnet-ap-southeast-2a (10.203.104.128/26)  ← PRIVATE (shared)
│   │       64 IPs. Shared internal resources across teams.
│   │
│   ├── 🔵 ITAM-NP-PRIVATESUBNET-NONPROD-A    (100.94.0.0/19)   ← PRIVATE
│   │       8,192 IPs! This is where your ECS tasks, app servers run.
│   │       (Note: this uses a DIFFERENT CIDR range — likely a secondary CIDR on the VPC)
│   │
│   └── (10.203.107.0/24) — another private subnet
│         256 IPs.
│
├── ap-southeast-2b (Data Center B)
│   ├── 🟢 ITAM-NP-PUBLICSUBNET-NONPROD-B     (10.203.104.128/26)  ← PUBLIC
│   │       64 IPs. Second ALB node sits here for redundancy.
│   │
│   ├── 🔵 nonprod-shared-internal-subnet-ap-southeast-2b  ← PRIVATE (shared)
│   │
│   └── (100.94.32.0/19) — PRIVATE
│         8,192 IPs. More app servers / ECS tasks.
│
└── ap-southeast-2c (Data Center C)
    ├── 🟢 ITAM-NP-PUBLICSUBNET-NONPROD-C     (10.203.104.64/26)  ← PUBLIC
    │       64 IPs. Third ALB node for even more redundancy.
    │
    └── (additional private subnets)

🟢 = Public Subnet (can receive traffic from the internet)
🔵 = Private Subnet (hidden from the internet, protected)
```

**Why does this matter to you as an SDE-3?**

When you deploy an ECS service, you choose WHICH subnets it runs in. If you pick only `ap-southeast-2a`, and that data center has an issue, your service goes down. If you pick all 3 AZs, your service survives any single AZ failure.

```
GOOD (High Availability):
  ECS Service → subnets: [private-2a, private-2b, private-2c]
  Your tasks spread across 3 data centers. One can die, you're fine.

BAD (Single point of failure):
  ECS Service → subnets: [private-2a]
  All tasks in one data center. If 2a goes down, you're down.
```

---

### 2.3 Public vs Private Subnets — Tracing Your API URL to Actual IPs

Let's take your real URL and backtrack through every layer to find exactly which
subnets and IPs it's hosted on.

```
YOUR URL: https://api-ap-southeast-2.nonprod-nielsen-iwatch.com/batch-processing-service/v1/api

Let's reverse-engineer where this lives, step by step.
You can follow along in your AWS Console right now.
```

**STEP 1: Find the ALB behind the domain name**

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Go to: Route 53 → Hosted Zones → nonprod-nielsen-iwatch.com            │
│ (You already did this! Here's what you found)                           │
│                                                                         │
│ Hosted Zone: nonprod-nielsen-iwatch.com                                 │
│ Type: PUBLIC zone                                                       │
│ Records: 177 total (that's a lot of microservices!)                     │
│                                                                         │
│ You filtered for "api-ap-southeast" and found 3 records:               │
│                                                                         │
│ ┌───────────────────────────────────────────────────────────────────┐   │
│ │ RECORD 1 (YOUR batch-processing-service lives here):              │   │
│ │                                                                   │   │
│ │ Name:  api-ap-southeast-2.nonprod-nielsen-iwatch.com              │   │
│ │ Type:  A (ALIAS)                                                  │   │
│ │ Routing: Simple                                                   │   │
│ │ Alias: Yes                                                        │   │
│ │ Target: internal-nonprod-fusion-shared-alb-244036836              │   │
│ │         .ap-southeast-2.elb.amazonaws.com                         │   │
│ │                                                                   │   │
│ │ ⚠️  IMPORTANT DISCOVERY: The ALB name starts with "internal-"!    │   │
│ │ This means it's an INTERNAL ALB (scheme: internal), NOT           │   │
│ │ internet-facing! More on this below — this changes everything.    │   │
│ ├───────────────────────────────────────────────────────────────────┤   │
│ │ RECORD 2 (staging wildcard):                                      │   │
│ │                                                                   │   │
│ │ Name:  *.stage.api-ap-southeast-2.nonprod-nielsen-iwatch.com      │   │
│ │ Type:  A (ALIAS)                                                  │   │
│ │ Target: internal-nonprod-fusion-olcd-common-nlb-10407d3473        │   │
│ │         .ap-southeast-2.elb.amazonaws.com                         │   │
│ │                                                                   │   │
│ │ This is a DIFFERENT load balancer — an NLB (Network Load Balancer)│   │
│ │ for staging. The wildcard *.stage means:                          │   │
│ │   anything.stage.api-ap-southeast-2.nonprod-nielsen-iwatch.com    │   │
│ │ routes to this NLB. Used for staging/testing environments.        │   │
│ ├───────────────────────────────────────────────────────────────────┤   │
│ │ RECORD 3 (fusion API — same ALB as record 1):                     │   │
│ │                                                                   │   │
│ │ Name:  fusion-api-ap-southeast-2.nonprod-nielsen-iwatch.com       │   │
│ │ Type:  A (ALIAS)                                                  │   │
│ │ Target: internal-nonprod-fusion-shared-alb-244036836              │   │
│ │         .ap-southeast-2.elb.amazonaws.com                         │   │
│ │                                                                   │   │
│ │ Points to the SAME ALB as record 1! This means the same ALB      │   │
│ │ serves both "api-ap-southeast-2" and "fusion-api-ap-southeast-2"  │   │
│ │ domains. The ALB uses HOST-BASED routing to tell them apart.      │   │
│ └───────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│ KEY INSIGHT: 177 records in this hosted zone means your company has     │
│ ~177 DNS entries — many microservices, environments, and endpoints      │
│ all under nonprod-nielsen-iwatch.com.                                   │
└─────────────────────────────────────────────────────────────────────────┘
```

**🔴 BIG DISCOVERY: Your ALB is INTERNAL, not internet-facing!**

```
The ALB name is: internal-nonprod-fusion-shared-alb-244036836.ap-southeast-2.elb.amazonaws.com
                 ^^^^^^^^
                 This prefix "internal-" means the ALB is INTERNAL.

What does this change?

INTERNET-FACING ALB (what we assumed before):
  Internet → Internet Gateway → ALB (public subnet) → ECS task (private subnet)
  ALB has PUBLIC IPs. Anyone on the internet can reach it.

INTERNAL ALB (what you ACTUALLY have):
  The ALB sits in PRIVATE subnets. It has only PRIVATE IPs.
  It is NOT directly reachable from the internet.

So how does traffic reach your API? There must be something IN FRONT of the ALB:

┌─────────────────────────────────────────────────────────────────────────┐
│ LIKELY ARCHITECTURE (your company's setup):                              │
│                                                                          │
│ Option A: CloudFront → Internal ALB                                      │
│                                                                          │
│   Internet                                                               │
│      │                                                                   │
│      ▼                                                                   │
│   CloudFront (CDN, edge locations worldwide)                             │
│      │                                                                   │
│      ▼ (origin: internal ALB via VPC origin or Lambda@Edge)              │
│   Internal ALB: internal-nonprod-fusion-shared-alb-244036836             │
│      │          (in PRIVATE subnets, no public IP)                       │
│      ▼                                                                   │
│   ECS Tasks (in PRIVATE subnets)                                         │
│                                                                          │
│ Option B: API Gateway → VPC Link → Internal ALB/NLB                      │
│                                                                          │
│   Internet                                                               │
│      │                                                                   │
│      ▼                                                                   │
│   API Gateway (AWS managed, public endpoint)                             │
│      │                                                                   │
│      ▼ (VPC Link — private connection into your VPC)                     │
│   Internal ALB: internal-nonprod-fusion-shared-alb-244036836             │
│      │          (in PRIVATE subnets)                                     │
│      ▼                                                                   │
│   ECS Tasks (in PRIVATE subnets)                                         │
│                                                                          │
│ Option C: External ALB/NLB → Internal ALB                                │
│                                                                          │
│   Internet                                                               │
│      │                                                                   │
│      ▼                                                                   │
│   External NLB or ALB (in PUBLIC subnets, internet-facing)               │
│      │                                                                   │
│      ▼                                                                   │
│   Internal ALB: internal-nonprod-fusion-shared-alb-244036836             │
│      │          (in PRIVATE subnets)                                     │
│      ▼                                                                   │
│   ECS Tasks (in PRIVATE subnets)                                         │
│                                                                          │
│ Option D: VPN / Direct Connect only (no public internet access)          │
│                                                                          │
│   Your office network / VPN                                              │
│      │                                                                   │
│      ▼ (private connection, never touches the internet)                  │
│   Internal ALB: internal-nonprod-fusion-shared-alb-244036836             │
│      │          (in PRIVATE subnets)                                     │
│      ▼                                                                   │
│   ECS Tasks (in PRIVATE subnets)                                         │
│                                                                          │
│   This is common for NONPROD environments — only accessible via VPN.     │
│   You can only hit api-ap-southeast-2.nonprod-nielsen-iwatch.com         │
│   when connected to your company's VPN.                                  │
│                                                                          │
│ ┌─────────────────────────────────────────────────────────────────┐      │
│ │ HOW TO FIND OUT which option your company uses:                  │      │
│ │                                                                  │      │
│ │ 1. Try hitting the URL WITHOUT VPN connected.                    │      │
│ │    If it works → Option A, B, or C (something public in front)   │      │
│ │    If it fails → Option D (VPN-only, internal ALB)               │      │
│ │                                                                  │      │
│ │ 2. Go to: EC2 → Load Balancers → search for                     │      │
│ │    "nonprod-fusion-shared-alb-244036836"                         │      │
│ │    Check the "Scheme" field:                                     │      │
│ │      "internal" = confirmed internal ALB                         │      │
│ │    Check the "Subnets" — they'll be PRIVATE subnets              │      │
│ │                                                                  │      │
│ │ 3. Check if there's a CloudFront distribution:                   │      │
│ │    CloudFront → Distributions → search for your domain           │      │
│ │                                                                  │      │
│ │ 4. Check if there's an API Gateway:                              │      │
│ │    API Gateway → APIs → look for one with your domain            │      │
│ └─────────────────────────────────────────────────────────────────┘      │
└─────────────────────────────────────────────────────────────────────────┘
```

**What "shared" ALB means:**

```
ALB name: internal-nonprod-fusion-SHARED-alb-244036836

"shared" = this ONE ALB serves MULTIPLE microservices.

From your Route 53 records, we can already see:
  api-ap-southeast-2.nonprod-nielsen-iwatch.com        → this ALB
  fusion-api-ap-southeast-2.nonprod-nielsen-iwatch.com  → SAME ALB

The ALB uses a combination of:
  HOST-BASED routing: "if Host header = api-ap-southeast-2.nonprod-nielsen-iwatch.com"
  PATH-BASED routing: "if path = /batch-processing-service/*"

to decide which target group (which microservice) gets the request.

This is cost-efficient: one ALB (~$22/month) serves many services,
instead of one ALB per service ($22 × N services).

┌─────────────────────────────────────────────────────────────────┐
│ Go to: EC2 → Load Balancers → find this ALB                     │
│ Click "Listeners" → HTTPS:443 → "View/edit rules"               │
│                                                                  │
│ You'll see ALL the routing rules — every microservice that       │
│ shares this ALB. Something like:                                 │
│                                                                  │
│ Rule 1: Host = api-ap-southeast-2.nonprod-nielsen-iwatch.com     │
│         AND path = /batch-processing-service/*                   │
│         → forward to: batch-processing-svc-tg                    │
│                                                                  │
│ Rule 2: Host = api-ap-southeast-2.nonprod-nielsen-iwatch.com     │
│         AND path = /user-service/*                               │
│         → forward to: user-svc-tg                                │
│                                                                  │
│ Rule 3: Host = fusion-api-ap-southeast-2.nonprod-nielsen-...     │
│         AND path = /some-fusion-service/*                        │
│         → forward to: fusion-svc-tg                              │
│                                                                  │
│ This is your company's entire microservice routing map!          │
│ Screenshot it. Study it. Know every service that shares this ALB.│
└─────────────────────────────────────────────────────────────────┘
```

**The staging NLB — what's that about?**

```
Record: *.stage.api-ap-southeast-2.nonprod-nielsen-iwatch.com
Target: internal-nonprod-fusion-olcd-common-nlb-10407d3473.ap-southeast-2.elb.amazonaws.com

This is a DIFFERENT load balancer — an NLB (Network Load Balancer), not ALB.

NLB vs ALB:
  ALB = Layer 7 (understands HTTP, paths, headers, cookies)
  NLB = Layer 4 (just forwards TCP/UDP packets, ultra-fast, static IPs)

The wildcard *.stage means ANY subdomain under stage works:
  v1.stage.api-ap-southeast-2.nonprod-nielsen-iwatch.com
  test.stage.api-ap-southeast-2.nonprod-nielsen-iwatch.com
  my-feature-branch.stage.api-ap-southeast-2.nonprod-nielsen-iwatch.com

This is likely used for:
  - Feature branch deployments (each branch gets its own subdomain)
  - Staging/QA testing before promoting to the main nonprod environment
  - The NLB might route to different ECS services based on the subdomain
    (using ECS Service Connect or a reverse proxy like Nginx/Envoy)
```

**STEP 3: Find the target group (which ECS tasks receive traffic)**

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Still on the ALB page → click "Listeners" tab                           │
│ → click the HTTPS:443 listener → "View/edit rules"                      │
│                                                                         │
│ Find the rule:                                                          │
│ ┌───────────────────────────────────────────────────────────────┐       │
│ │ IF path is /batch-processing-service/*                        │       │
│ │ THEN forward to: batch-processing-svc-tg                      │       │
│ └───────────────────────────────────────────────────────────────┘       │
│                                                                         │
│ Now go to: EC2 → Target Groups → batch-processing-svc-tg               │
│ Click "Targets" tab:                                                    │
│                                                                         │
│ ┌───────────────────────────────────────────────────────────────┐       │
│ │ Target          │ Port │ AZ              │ Health  │ Zone     │       │
│ │ 10.203.107.15   │ 8080 │ ap-southeast-2a │ healthy │ private  │       │
│ │ 10.203.107.42   │ 8080 │ ap-southeast-2b │ healthy │ private  │       │
│ │ 10.203.107.78   │ 8080 │ ap-southeast-2a │ healthy │ private  │       │
│ └───────────────────────────────────────────────────────────────┘       │
│                                                                         │
│ FOUND YOUR CONTAINERS! These are the actual ECS tasks running           │
│ your batch-processing-service code.                                     │
│                                                                         │
│ Let's figure out which subnet each IP belongs to:                       │
│                                                                         │
│   10.203.107.15 → falls in 10.203.107.0/24 (256 IPs: .0 to .255)       │
│                 → this is the private subnet in AZ 2a                   │
│                 → ITAM-NP-PRIVATESUBNET-NONPROD-A or the 10.203.107.0   │
│                    subnet you saw in the VPC resource map               │
│                                                                         │
│   10.203.107.42 → same CIDR range, but ECS placed it in AZ 2b          │
│                 → (or it could be in the 100.94.32.0/19 range           │
│                    if the IP is in that range instead)                   │
│                                                                         │
│   The exact subnet depends on your actual IPs — check the target        │
│   group in your console to see the real IPs and AZs.                    │
└─────────────────────────────────────────────────────────────────────────┘
```

**STEP 4: Find the ECS task details**

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Go to: ECS → Clusters → your cluster → Services →                       │
│        batch-processing-service → "Tasks" tab                            │
│                                                                         │
│ Click on any running task. You'll see:                                  │
│                                                                         │
│ ┌───────────────────────────────────────────────────────────────┐       │
│ │ Task ID:        abc123-def456-ghi789                          │       │
│ │ Task Definition: batch-processing-service:42                  │       │
│ │ Launch Type:    FARGATE                                       │       │
│ │ Platform:       1.4.0                                         │       │
│ │ Last Status:    RUNNING                                       │       │
│ │ Health Status:  HEALTHY                                       │       │
│ │                                                               │       │
│ │ Network:                                                      │       │
│ │   Private IP:   10.203.107.15                                 │       │
│ │   Subnet:       subnet-0abc123 (ITAM-NP-PRIVATESUBNET-        │       │
│ │                 NONPROD-A, 10.203.107.0/24, AZ 2a)            │       │
│ │   Security Group: sg-0def456 (batch-processing-svc-sg)        │       │
│ │   ENI:          eni-0ghi789 (the network interface)            │       │
│ │                                                               │       │
│ │ Container:                                                    │       │
│ │   Image: 123456789.dkr.ecr.ap-southeast-2.amazonaws.com/      │       │
│ │          batch-processing-service:v1.2.3                      │       │
│ │   Port:  8080                                                 │       │
│ └───────────────────────────────────────────────────────────────┘       │
│                                                                         │
│ NOW YOU KNOW EXACTLY:                                                   │
│   - Which subnet this task is in (PRIVATESUBNET-NONPROD-A)              │
│   - Which AZ (ap-southeast-2a)                                          │
│   - Its private IP (10.203.107.15)                                      │
│   - Which security group controls its traffic                           │
│   - Which Docker image version it's running                             │
└─────────────────────────────────────────────────────────────────────────┘
```

**The complete map — from URL to IP to subnet (UPDATED with real data):**

```
https://api-ap-southeast-2.nonprod-nielsen-iwatch.com/batch-processing-service/v1/api
    │  (called directly from Postman / client while on VPN)
    │
    │ DNS (Route 53 — resolves only on VPN / inside VPC)
    │ hosted zone: nonprod-nielsen-iwatch.com (177 records)
    ▼
Internal ALB: internal-nonprod-fusion-shared-alb-244036836
    │  (in PRIVATE subnets — NOT internet-facing)
    │
    │  Also serves (same ALB, different host rules):
    │  fusion-api-ap-southeast-2.nonprod-nielsen-iwatch.com → same ALB
    │
    │  Staging uses a different LB:
    │  *.stage.api-ap-southeast-2.nonprod-nielsen-iwatch.com → NLB
    │
    │  ALB routing: host + path → target group
    │  /batch-processing-service/* → batch-processing-svc-tg
    ▼
Target Group: batch-processing-svc-tg
    │
    │ ECS tasks in PRIVATE subnets:
    ├── 10.203.107.x:8080  → PRIVATESUBNET-NONPROD-A  AZ 2a
    ├── 10.203.107.x:8080  → PRIVATESUBNET-NONPROD-B  AZ 2b
    └── 10.203.107.x:8080  → PRIVATESUBNET-NONPROD-A  AZ 2a
    │
    │ Each task runs Docker image from ECR:
    │ 123456789.dkr.ecr.ap-southeast-2.amazonaws.com/batch-processing-service:v1.2.3
    │
    ▼
Your code handles the request, talks to:
  S3 → via VPC endpoint vpce-08219d5eb0951efba (free, private)
  DynamoDB → via VPC endpoint vpce-04b5230053faec1ff (free, private)
  External APIs → via NAT Gateway (costs $0.045/GB)

Response flows back:
  ECS Task → Internal ALB → VPN tunnel → Your Postman

ALTERNATIVE PATH (for external clients without VPN):
  External client → API Gateway (beta.aws-itam-nonprod.nielsencsp.com)
  → VPC Link → same Internal ALB → same ECS tasks
```

**How to do this yourself for ANY URL in your company:**
```
1. Route 53 → Hosted Zones → find the domain  → see the ALIAS target (ALB/NLB name)
2. Check the ALB name: starts with "internal-"? → it's an internal ALB (private subnets)
                        no "internal-" prefix?   → it's internet-facing (public subnets)
3. EC2 → Load Balancers → find it              → see Scheme, VPC, Subnets
4. Listeners → Rules                            → find which target group
5. Target Groups → Targets tab                  → see the PRIVATE IPs and AZs
6. ECS → Tasks → click the task                 → see exact subnet, SG, image version

This is the #1 debugging skill for an SDE-3.
"Where is my traffic actually going?" — now you know how to answer that.
```

---

**Public vs Private — the core concept (updated with your real architecture):**

```
PUBLIC SUBNETS:
  ├── Have a route to Internet Gateway (0.0.0.0/0 → igw-xxx)
  ├── Resources CAN get public IPs
  ├── Reachable from the internet
  ├── In your case: PUBLICSUBNET-NONPROD-A/B/C (10.203.104.0/26, .64/26, .128/26)
  └── Used for: internet-facing resources (if any), NAT Gateways sit here

PRIVATE SUBNETS (where BOTH your ALB and ECS tasks live):
  ├── Have a route to NAT Gateway (0.0.0.0/0 → nat-xxx)
  ├── Resources CANNOT get public IPs
  ├── NOT reachable from the public internet
  ├── CAN reach the internet outbound (via NAT, for external API calls)
  ├── In your case: PRIVATESUBNET-NONPROD-A/B (10.203.107.0/24, 100.94.x.x/19)
  └── Your internal ALB (internal-nonprod-fusion-shared-alb) also lives here

WHY your company uses an INTERNAL ALB:
  ├── Extra security: the ALB itself is not exposed to the internet
  ├── Traffic enters through a controlled gateway (CloudFront/API GW/VPN)
  ├── Common pattern for nonprod: only accessible via company VPN
  └── Defense in depth: even if someone finds the ALB DNS name,
      they can't reach it without being inside the VPC or on VPN
```

---

### 2.4 Route Tables — The Traffic Signs

Route tables tell AWS: "When traffic wants to go to X destination, send it through Y."

**Your VPC has 5 route tables (from the screenshot):**

```
1. ITAM-NP-PUBLICROUTE-NONPROD-AP-SOUTHEAST-2
   ├── Used by: all 3 public subnets
   ├── 5 subnet associations, 6 routes
   │
   │  Key route:
   │  ┌──────────────────┬────────────────────────────────────────┐
   │  │ Destination      │ Target                                 │
   │  ├──────────────────┼────────────────────────────────────────┤
   │  │ 10.203.104.0/22  │ local (stay within VPC)                │
   │  │ 0.0.0.0/0        │ igw-xxxxx (Internet Gateway)           │
   │  └──────────────────┴────────────────────────────────────────┘
   │
   │  Translation: "If the destination is inside our VPC, route locally.
   │                For EVERYTHING ELSE (0.0.0.0/0), go to the internet."
   │
   │  This is what makes it a PUBLIC subnet — it has a route to the Internet Gateway.

2. ITAM-NP-DEFAULTROUTE-NONPROD-AP-SOUTHEAST-2
   ├── Used by: subnets that don't have a specific route table
   └── Probably routes to NAT Gateway or stays local-only

3. ITAM-NP-NATROUTE-NONPROD-AP-SOUTHEAST-2A
   ├── Used by: private subnets in AZ 2a
   │
   │  Key route:
   │  ┌──────────────────┬────────────────────────────────────────┐
   │  │ Destination      │ Target                                 │
   │  ├──────────────────┼────────────────────────────────────────┤
   │  │ 10.203.104.0/22  │ local (stay within VPC)                │
   │  │ 0.0.0.0/0        │ nat-xxxxx (NAT Gateway in AZ 2a)       │
   │  └──────────────────┴────────────────────────────────────────┘
   │
   │  Translation: "For internet-bound traffic, go through NAT Gateway.
   │                NAT lets you GO OUT but nobody can COME IN."

4. ITAM-NP-NATROUTE-NONPRODAP-SOUTHEAST-2B  (same idea, for AZ 2b)

5. ITAM-NP-NATROUTE-NONPRODAP-SOUTHEAST-2C  (same idea, for AZ 2c)
```

**Why one NAT route table per AZ?**
```
If AZ-2a's NAT Gateway dies:
  ├── Private subnets in 2a lose internet access
  ├── But private subnets in 2b and 2c still work (they use their own NAT)
  └── Your service stays up because tasks in 2b/2c can still reach external APIs

If you used ONE NAT Gateway for all AZs:
  ├── That single NAT dies → ALL private subnets lose internet
  └── Cross-AZ NAT traffic also costs more ($0.01/GB)
```

---

### 2.5 Network Connections — Your VPC's Doors and Windows

**From the screenshot, your VPC has 6 network connections:**

```
1. ITAM-NP-IGW-NONPROD-AP-SOUTHEAST-2
   │  TYPE: Internet Gateway
   │  WHAT: The main front door. Connects your VPC to the public internet.
   │  WHO USES IT: Public subnets (ALB, bastion hosts).
   │  ANALOGY: The main entrance of your office building.
   │
   │  "Internet routes to 3 public subnets.
   │   6 private subnets route to the Internet." (via NAT)

2. ITAM-NP-NGW-NONPROD-AP-SOUTHEAST-2A
   │  TYPE: NAT Gateway (in AZ 2a)
   │  WHAT: Back door for private subnets in AZ 2a.
   │  Public NAT gateway, 1 ENI with 1 EIP (Elastic IP).
   │  ANALOGY: A one-way mail slot. Your kitchen staff can send letters out,
   │           but nobody can push letters in.

3. ITAM-NP-NGW-NONPROD-AP-SOUTHEAST-2B
   │  TYPE: NAT Gateway (in AZ 2b)
   │  Same as above, for the second data center.

4. ITAM-NP-NGW-NONPROD-AP-SOUTHEAST-2C
   │  TYPE: NAT Gateway (in AZ 2c)
   │  Same as above, for the third data center.

5. vpce-08219d5eb0951efba
   │  TYPE: Gateway Endpoint → S3
   │  WHAT: A private tunnel from your VPC directly to S3.
   │  WHY: Without this, every S3 request from private subnets would go:
   │       App → NAT Gateway → Internet → S3 (costs $0.045/GB via NAT!)
   │       With this endpoint:
   │       App → VPC Endpoint → S3 (FREE, faster, never leaves AWS network)
   │  ANALOGY: Instead of mailing a letter to the S3 warehouse across town,
   │           you have a private underground tunnel directly to it.

6. vpce-04b5230053faec1ff
   │  TYPE: Gateway Endpoint → DynamoDB
   │  WHAT: Same idea as S3 endpoint, but for DynamoDB.
   │  WHY: Free, fast, private access to DynamoDB without going through NAT.
```

**Cost example — why VPC endpoints matter:**
```
Without S3 VPC Endpoint:
  Your app downloads 1TB/month from S3
  Route: App → NAT Gateway → Internet → S3
  NAT Gateway processing: 1,000 GB × $0.045 = $45/month JUST for NAT

With S3 VPC Endpoint:
  Route: App → VPC Endpoint → S3
  Cost: $0 (Gateway endpoints are free)
  Savings: $45/month = $540/year, and it's faster too
```

---

### 2.6 Putting It All Together — YOUR Actual Microservice Request Flow

Your system has TWO URLs. Let's understand both and how they relate:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ URL 1 — MICROSERVICE URL (what you call from Postman / client):              │
│                                                                              │
│   "JobQueueService":                                                         │
│   "https://api-ap-southeast-2.nonprod-nielsen-iwatch.com                     │
│    /batch-processing-service/v1/api"                                         │
│   │       │                   │                        │                     │
│   │       │                   │                        └── /batch-processing  │
│   │       │                   │                             -service/v1/api   │
│   │       │                   │                             The actual API    │
│   │       │                   │                             endpoint path     │
│   │       │                   │                                               │
│   │       │                   └── nonprod-nielsen-iwatch.com                  │
│   │       │                        Internal domain, resolvable when you're    │
│   │       │                        on company VPN (or from inside the VPC)    │
│   │       │                                                                   │
│   │       └── api-ap-southeast-2 = API endpoint in Sydney region             │
│   │                                                                           │
│   └── This is what YOU call from Postman (while on VPN).                     │
│       This is what other microservices call internally.                       │
│       This hits the INTERNAL ALB directly.                                   │
│                                                                              │
│   "JobQueueService" is the service name in your app's config/registry.       │
│                                                                              │
├─────────────────────────────────────────────────────────────────────────────┤
│ URL 2 — GATEWAY URL (used in a different context):                           │
│                                                                              │
│   GatewayURL = https://beta.aws-itam-nonprod.nielsencsp.com                  │
│   │             │    │        │        │                                      │
│   │             │    │        │        └── nielsencsp.com = Nielsen Cloud     │
│   │             │    │        │             Services Platform (public domain) │
│   │             │    │        └── nonprod = non-production                    │
│   │             │    └── aws-itam = IT Asset Management platform             │
│   │             └── beta = beta/testing stage                                │
│   │                                                                          │
│   └── This is the API Gateway — a public-facing entry point.                 │
│       Used by: external clients, frontend apps, or services that             │
│       don't have direct VPC access.                                          │
│       The Gateway then calls the internal microservice URL behind the scenes.│
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘

HOW THESE TWO URLs RELATE:

  ┌─────────────────────────────────────────────────────────────────────┐
  │                                                                     │
  │  PATH A — Direct call (what YOU do from Postman on VPN):            │
  │                                                                     │
  │  You (on VPN) ──► Internal ALB ──► ECS Task                        │
  │  URL: https://api-ap-southeast-2.nonprod-nielsen-iwatch.com/...     │
  │  VPN gives you access to the internal DNS and private network.      │
  │  No API Gateway involved. Direct to the ALB.                        │
  │                                                                     │
  │  PATH B — Via Gateway (external clients / frontend apps):           │
  │                                                                     │
  │  External Client ──► API Gateway ──► VPC Link ──► Internal ALB ──► ECS Task
  │  URL: https://beta.aws-itam-nonprod.nielsencsp.com/...              │
  │  Gateway handles auth, rate limiting, then forwards to the same     │
  │  internal ALB. The client never sees the internal URL.              │
  │                                                                     │
  │  BOTH paths end up at the same place:                               │
  │  internal-nonprod-fusion-shared-alb-244036836 → ECS tasks           │
  │                                                                     │
  └─────────────────────────────────────────────────────────────────────┘

TWO DOMAINS, TWO PURPOSES:
  nielsencsp.com             = public-facing (API Gateway, for external access)
  nonprod-nielsen-iwatch.com = internal (ALB + microservices, VPN / VPC access)
```

**Now here's the FULL journey when YOU call the API from Postman (Path A):**

```
╔══════════════════════════════════════════════════════════════════════════════╗
║  STEP 1: You Call the Microservice URL (from Postman, on VPN)                ║
║  URL: https://api-ap-southeast-2.nonprod-nielsen-iwatch.com                  ║
║       /batch-processing-service/v1/api                                       ║
╠══════════════════════════════════════════════════════════════════════════════╣
║                                                                              ║
║  From Postman (or curl, or your code running locally):                       ║
║                                                                              ║
║    POST https://api-ap-southeast-2.nonprod-nielsen-iwatch.com                ║
║         /batch-processing-service/v1/api                                     ║
║    Headers: { Content-Type: "application/json", ... }                        ║
║    Body: { "jobType": "process-data", ... }                                  ║
║                                                                              ║
║  Your machine is connected to the company VPN.                               ║
║  VPN gives you:                                                              ║
║    1. Access to internal DNS (can resolve nonprod-nielsen-iwatch.com)        ║
║    2. A route into the VPC (your traffic tunnels through VPN into the VPC)   ║
║                                                                              ║
║  DNS Resolution (Route 53 — private hosted zone or split-horizon DNS):       ║
║    api-ap-southeast-2.nonprod-nielsen-iwatch.com                             ║
║    → ALIAS: internal-nonprod-fusion-shared-alb-244036836                     ║
║             .ap-southeast-2.elb.amazonaws.com                                ║
║    → Resolves to PRIVATE IPs (e.g., 10.203.x.x)                             ║
║                                                                              ║
║  Without VPN: this DNS lookup would FAIL (or return nothing).                ║
║  The domain only resolves inside the VPC / via VPN.                          ║
║                                                                              ║
║  ┌─────────────────────────────────────────────────────────────────┐         ║
║  │ Try it: disconnect VPN, then run:                                │         ║
║  │   nslookup api-ap-southeast-2.nonprod-nielsen-iwatch.com        │         ║
║  │ It should fail or return no results.                             │         ║
║  │                                                                  │         ║
║  │ Reconnect VPN, run the same command:                             │         ║
║  │ It should return private IPs (10.x.x.x).                        │         ║
║  │ Those IPs belong to the internal ALB.                            │         ║
║  └─────────────────────────────────────────────────────────────────┘         ║
╚══════════════════════════════════════════════════════════════════════════════╝
                                    │
                                    ▼
╔══════════════════════════════════════════════════════════════════════════════╗
║  STEP 2: Internal ALB (Shared Application Load Balancer)                     ║
║  WHO: internal-nonprod-fusion-shared-alb-244036836                           ║
║  WHERE: PRIVATE subnets inside ITAM-NP-VPC-AP-SOUTHEAST-2                    ║
╠══════════════════════════════════════════════════════════════════════════════╣
║                                                                              ║
║  Your request arrives at the internal ALB (via VPN tunnel).                  ║
║  The ALB sees:                                                               ║
║    Host: api-ap-southeast-2.nonprod-nielsen-iwatch.com                       ║
║    Path: /batch-processing-service/v1/api                                    ║
║                                                                              ║
║  This is a SHARED ALB — it serves multiple domains and services.             ║
║  It uses HOST-BASED + PATH-BASED routing:                                    ║
║                                                                              ║
║  ┌────────────────────────────────────────────────────────────────┐          ║
║  │ Rule 1: IF host = api-ap-southeast-2.nonprod-nielsen-iwatch.com│          ║
║  │         AND path = /batch-processing-service/*                 │          ║
║  │         THEN forward to → Target Group: batch-processing-svc   │          ║
║  │                                                                │          ║
║  │ Rule 2: IF host = api-ap-southeast-2.nonprod-nielsen-iwatch.com│          ║
║  │         AND path = /user-service/*                             │          ║
║  │         THEN forward to → Target Group: user-svc               │          ║
║  │                                                                │          ║
║  │ Rule 3: IF host = fusion-api-ap-southeast-2.nonprod-nielsen-...│          ║
║  │         AND path = /some-fusion-endpoint/*                     │          ║
║  │         THEN forward to → Target Group: fusion-svc             │          ║
║  │                                                                │          ║
║  │ Default: return 404                                            │          ║
║  └────────────────────────────────────────────────────────────────┘          ║
║                                                                              ║
║  Your request matches Rule 1 → forwarded to batch-processing-svc target group║
║                                                                              ║
║  ┌─────────────────────────────────────────────────────────────────┐         ║
║  │ Go check: EC2 → Load Balancers → search for                     │         ║
║  │ "nonprod-fusion-shared-alb-244036836"                            │         ║
║  │ Confirm: Scheme = "internal", VPC = ITAM-NP-VPC-AP-SOUTHEAST-2  │         ║
║  │ Click "Listeners" → HTTPS:443 → "View/edit rules"               │         ║
║  │ You'll see ALL microservices routed through this single ALB.     │         ║
║  └─────────────────────────────────────────────────────────────────┘         ║
╚══════════════════════════════════════════════════════════════════════════════╝
                                    │
                                    ▼
╔══════════════════════════════════════════════════════════════════════════════╗
║  STEP 4: Target Group                                                        ║
║  WHO: batch-processing-svc target group                                      ║
╠══════════════════════════════════════════════════════════════════════════════╣
║                                                                              ║
║  A target group is a list of "healthy backends" that can handle requests.    ║
║  In your case, these are ECS tasks (containers) running your                 ║
║  batch-processing-service.                                                   ║
║                                                                              ║
║  Target Group: batch-processing-svc                                          ║
║  ├── Target 1: 10.203.107.15:8080 (ECS task in AZ 2a) ✅ healthy            ║
║  ├── Target 2: 10.203.107.42:8080 (ECS task in AZ 2b) ✅ healthy            ║
║  └── Target 3: 10.203.107.78:8080 (ECS task in AZ 2a) ✅ healthy            ║
║                                                                              ║
║  The ALB picks one of these healthy targets (round-robin or least            ║
║  outstanding requests) and forwards your request to it.                      ║
║                                                                              ║
║  Notice the IPs: 10.203.107.x — these are PRIVATE IPs inside your VPC.      ║
║  These tasks are in PRIVATESUBNET-NONPROD-A/B. No public IP. Safe.           ║
║                                                                              ║
║  The target group also runs HEALTH CHECKS:                                   ║
║  Every 30 seconds it calls GET /batch-processing-service/v1/health           ║
║  If a task fails 3 health checks in a row → marked unhealthy → no traffic.  ║
║                                                                              ║
║  ┌─────────────────────────────────────────────────────────────────┐         ║
║  │ Go check: EC2 → Target Groups → find batch-processing-svc      │         ║
║  │ Click "Targets" tab — see your registered ECS tasks             │         ║
║  │ Click "Health checks" tab — see the health check configuration  │         ║
║  └─────────────────────────────────────────────────────────────────┘         ║
╚══════════════════════════════════════════════════════════════════════════════╝
                                    │
                                    ▼
╔══════════════════════════════════════════════════════════════════════════════╗
║  STEP 5: ECS Task (Your Container)                                           ║
║  WHERE: PRIVATESUBNET-NONPROD-A (10.203.107.0/24) or similar                 ║
╠══════════════════════════════════════════════════════════════════════════════╣
║                                                                              ║
║  Your batch-processing-service container receives the request on port 8080.  ║
║  The path it sees: /batch-processing-service/v1/api                          ║
║  (or just /v1/api if the ALB strips the prefix — depends on config)          ║
║                                                                              ║
║  Now your code runs. Let's say it needs to:                                  ║
║  a) Read a config file from S3                                               ║
║  b) Write job results to DynamoDB                                            ║
║  c) Call another internal microservice                                       ║
║  d) Call an external third-party API                                         ║
║                                                                              ║
║  Each of these takes a DIFFERENT network path:                               ║
╚══════════════════════════════════════════════════════════════════════════════╝
          │            │            │            │
          ▼            ▼            ▼            ▼
  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────┐
  │ (a) S3   │ │(b) Dynamo│ │(c) Other │ │(d) External  │
  │          │ │          │ │microsvce │ │API           │
  └────┬─────┘ └────┬─────┘ └────┬─────┘ └──────┬───────┘
       │            │            │               │
       ▼            ▼            ▼               ▼

  (a) S3 Request — via VPC Endpoint (FREE, fast):
      ECS Task (10.203.107.15)
        → VPC Endpoint vpce-08219d5eb0951efba
        → S3 bucket (api-ap-southeast-2.nonprod-nielsen-iwatch.com)
      NEVER leaves AWS network. No NAT. No internet. $0 data transfer.

  (b) DynamoDB Request — via VPC Endpoint (FREE, fast):
      ECS Task (10.203.107.15)
        → VPC Endpoint vpce-04b5230053faec1ff
        → DynamoDB table
      Same idea. Private tunnel. Free.

  (c) Internal Microservice Call (e.g., calling user-service):
      Two options your company might use:

      Option 1 — Via ALB (goes out and comes back in):
        ECS Task → NAT Gateway → Internet → ALB → user-service ECS task
        ❌ Wasteful! Traffic leaves VPC and comes back in. Costs NAT fees.

      Option 2 — Via Service Discovery / Internal ALB (stays inside VPC):
        ECS Task → Internal ALB (private, no internet) → user-service ECS task
        ✅ Traffic stays inside VPC. Fast. Free. This is the right way.

      Option 3 — Via ECS Service Connect / Cloud Map:
        ECS Task → user-service.local:8080 (DNS resolves to private IP)
        ✅ Direct service-to-service. Best option.

  (d) External API Call — via NAT Gateway (costs money):
      ECS Task (10.203.107.15)
        → Route table says: 0.0.0.0/0 → NAT Gateway
        → ITAM-NP-NGW-NONPROD-AP-SOUTHEAST-2A
        → Internet Gateway
        → External API (e.g., third-party data provider)
      NAT Gateway charges $0.045/GB for this traffic.

```

**Now let's talk about that S3 bucket name you mentioned:**

```
S3 Bucket: api-ap-southeast-2.nonprod-nielsen-iwatch.com

This bucket name matches your API domain name. This is a common pattern:

┌─────────────────────────────────────────────────────────────────────┐
│ WHY would an S3 bucket have the same name as your API domain?       │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│ Possibility 1: Static Asset Hosting                                 │
│   The bucket hosts static files (configs, templates, batch input    │
│   files) that your batch-processing-service reads.                  │
│   Your ECS task uses the AWS SDK to read from this bucket:          │
│                                                                     │
│   s3.get_object(                                                    │
│     Bucket="api-ap-southeast-2.nonprod-nielsen-iwatch.com",         │
│     Key="batch-jobs/config/job-template.json"                       │
│   )                                                                 │
│                                                                     │
│   This call goes through VPC Endpoint → S3. Free and fast.          │
│                                                                     │
│ Possibility 2: S3 Website Hosting + CloudFront                      │
│   The bucket serves as a CloudFront origin for the domain.          │
│   Static frontend (React/Angular) is in S3, APIs are on ALB.        │
│   CloudFront routes /batch-processing-service/* to ALB origin       │
│   and /* to S3 origin.                                              │
│                                                                     │
│ Possibility 3: Artifact Storage                                     │
│   Batch processing jobs read input from and write output to S3.     │
│   e.g., "process this CSV file" → read from S3 → process → write   │
│   results back to S3.                                               │
│                                                                     │
│ ┌─────────────────────────────────────────────────────────────┐     │
│ │ Go check: S3 → search for this bucket name                  │     │
│ │ Look at: what's inside? (folders, file types)                │     │
│ │ Look at: Bucket Policy (who can access it?)                  │     │
│ │ Look at: Properties → Static website hosting (enabled?)      │     │
│ │ Look at: Permissions → Block public access settings          │     │
│ └─────────────────────────────────────────────────────────────┘     │
└─────────────────────────────────────────────────────────────────────┘
```

**The complete picture — your batch-processing-service request, visualized:**

```
PATH A — YOU calling from Postman (on VPN):

You (on VPN, Postman)
│
│  POST https://api-ap-southeast-2.nonprod-nielsen-iwatch.com
│       /batch-processing-service/v1/api
│  Body: { "jobType": "process-data", "inputFile": "s3://bucket/input.csv" }
│
▼
┌─────────────────────────────────────────────────────────────────────────┐
│ VPN TUNNEL → DNS resolves to private IPs of Internal ALB                 │
│ (Route 53: api-ap-southeast-2.nonprod-nielsen-iwatch.com                 │
│  → ALIAS: internal-nonprod-fusion-shared-alb-244036836)                  │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
▼                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ INTERNAL ALB: internal-nonprod-fusion-shared-alb-244036836               │
│ (in PRIVATE subnets, NOT internet-facing)                                │
│                                                                          │
│ Host: api-ap-southeast-2.nonprod-nielsen-iwatch.com                      │
│ Rule: /batch-processing-service/* → Target Group: batch-processing-svc   │
│                                                                          │
│ Also serves: fusion-api-ap-southeast-2.nonprod-nielsen-iwatch.com        │
│ (same ALB, different host-based rules — shared ALB pattern)              │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
▼                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ TARGET GROUP: batch-processing-svc                                       │
│ ├── 10.203.107.x:8080 (AZ 2a) ✅                                        │
│ ├── 10.203.107.x:8080 (AZ 2b) ✅    ← ALB picks one                    │
│ └── 10.203.107.x:8080 (AZ 2a) ✅                                        │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
▼                               ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ ECS TASK: batch-processing-service (in PRIVATESUBNET-NONPROD-A)          │
│ Container: running on Fargate, private IP 10.203.107.x                   │
│                                                                          │
│ Your code receives the request and:                                      │
│                                                                          │
│ 1. Reads input file from S3                                              │
│    → VPC Endpoint (vpce-08219d5eb0951efba) → S3 bucket                   │
│    → s3://api-ap-southeast-2.nonprod-nielsen-iwatch.com/input.csv        │
│    → FREE, private, fast                                                 │
│                                                                          │
│ 2. Processes the data (CPU/memory work inside the container)             │
│                                                                          │
│ 3. Writes results to DynamoDB                                            │
│    → VPC Endpoint (vpce-04b5230053faec1ff) → DynamoDB table              │
│    → FREE, private, fast                                                 │
│                                                                          │
│ 4. Maybe publishes an event to SQS/SNS                                   │
│    → If VPC endpoint exists for SQS: private, fast                       │
│    → If no VPC endpoint: NAT Gateway → Internet → SQS ($0.045/GB)       │
│                                                                          │
│ 5. Returns HTTP 200 response with job ID                                 │
└───────────────────────────────┬─────────────────────────────────────────┘
                                │
▼                               ▼
Response flows back: ECS Task → Internal ALB → VPN tunnel → Your Postman


PATH B — External client via Gateway (different flow, same destination):

External Client (no VPN)
│
│  POST https://beta.aws-itam-nonprod.nielsencsp.com/some-route
│
▼
API Gateway (public, internet-facing)
│  Does: auth, rate limiting, routing
│  Maps request to internal service URL:
│  https://api-ap-southeast-2.nonprod-nielsen-iwatch.com
│  /batch-processing-service/v1/api
│
│  VPC Link (private tunnel via AWS PrivateLink)
▼
Internal ALB → Target Group → ECS Task (same as Path A from here)
│
▼
Response: ECS Task → Internal ALB → VPC Link → API Gateway → Client
```

---

### 2.7 The "NONPROD" in Your Names — Environment Separation

Notice everything says `NONPROD`:
```
ITAM-NP-VPC-AP-SOUTHEAST-2          (NP = Non-Production)
ITAM-NP-PUBLICSUBNET-NONPROD-A
ITAM-NP-PRIVATESUBNET-NONPROD-A
```

This means your company has SEPARATE VPCs for different environments:
```
┌─────────────────────────┐  ┌─────────────────────────┐
│ NONPROD VPC             │  │ PROD VPC                │
│ (what you're looking at)│  │ (production traffic)     │
│                         │  │                         │
│ ├── dev environment     │  │ ├── Real users           │
│ ├── staging environment │  │ ├── Real data            │
│ └── QA environment      │  │ └── Tighter security     │
│                         │  │                         │
│ CIDR: 10.203.104.0/22  │  │ CIDR: (different range)  │
└─────────────────────────┘  └─────────────────────────┘
         │                              │
         └──── VPC Peering or ──────────┘
              Transit Gateway
         (if they need to talk to each other)
```

**SDE-3 responsibility:** Never deploy to the wrong VPC. Your CI/CD pipeline should enforce this — dev branch → nonprod VPC, main branch → prod VPC.

---

### 2.8 Security Groups (Stateful Firewalls)

**Go to:** EC2 → Security Groups

Think of these as bouncers at each door. They are **stateful** — if you let someone in, their response is automatically allowed out.

```
EXAMPLE for your NONPROD setup:

ALB Security Group (bouncer at the front door):
  Inbound:  443 (HTTPS) from 0.0.0.0/0    ← anyone on the internet can connect
  Inbound:  80  (HTTP)  from 0.0.0.0/0     ← (redirects to HTTPS)
  Outbound: All traffic                     ← ALB can talk to anything inside VPC

App Security Group (bouncer at the kitchen door):
  Inbound:  8080 from ALB-SG               ← ONLY the ALB can talk to the app
  Outbound: All traffic                     ← app can reach DB, cache, internet (via NAT)

DB Security Group (bouncer at the vault):
  Inbound:  5432 from App-SG               ← ONLY the app can talk to the database
  Outbound: All traffic

Notice: App-SG references ALB-SG by ID, not by IP address.
This means "allow traffic from ANY resource that has the ALB security group attached."
If the ALB's IP changes (it does, frequently), the rule still works.
```

**Real-world example of a security group mistake:**
```
BAD: Someone adds "Inbound: 5432 from 0.0.0.0/0" to the DB security group
     → Your database is now accessible from the ENTIRE INTERNET
     → This is how data breaches happen

GOOD: "Inbound: 5432 from sg-0abc123 (App-SG)"
     → Only your application servers can reach the database
     → Even if someone gets into the public subnet, they can't reach the DB
```

---

### 2.9 NACLs (Network ACLs — Subnet-Level Firewalls)

NACLs are like security checkpoints at each floor of the building (subnet level), while Security Groups are bouncers at each room (resource level).

```
Key difference:
  Security Groups = STATEFUL  (allow inbound → response auto-allowed out)
  NACLs           = STATELESS (you must explicitly allow BOTH directions)

Example NACL rule:
  Rule 100: Allow inbound TCP 443 from 0.0.0.0/0     ← let HTTPS in
  Rule 100: Allow outbound TCP 1024-65535 to 0.0.0.0/0 ← let responses out
  (You need BOTH rules. Forget the outbound? Traffic gets blocked.)

Most companies (likely yours too) leave NACLs as default (allow all)
and rely on Security Groups for access control. NACLs are a backup layer.
```

---

### 2.10 VPC Peering & Transit Gateway

Your nonprod VPC might need to talk to other VPCs (e.g., a shared-services VPC with monitoring tools, or a data VPC with analytics databases).

```
VPC Peering (simple, direct connection):
  ┌──────────┐         ┌──────────┐
  │ NONPROD  │◄──────►│ SHARED   │
  │ VPC      │ peering │ SERVICES │
  └──────────┘         └──────────┘
  Good for: 2-3 VPCs that need to talk.
  Limitation: not transitive (A↔B and B↔C does NOT mean A↔C).

Transit Gateway (hub-and-spoke, for many VPCs):
                  ┌──────────────┐
  ┌──────────┐   │   Transit    │   ┌──────────┐
  │ NONPROD  │◄─►│   Gateway    │◄─►│  PROD    │
  └──────────┘   │   (hub)      │   └──────────┘
  ┌──────────┐   │              │   ┌──────────┐
  │ DATA     │◄─►│              │◄─►│ SHARED   │
  └──────────┘   └──────────────┘   └──────────┘
  Good for: many VPCs, centralized routing, network segmentation.
```

---

### 2.11 Action Items — Go Do These Now

```
□ Open VPC → Your VPCs → click on ITAM-NP-VPC-AP-SOUTHEAST-2
  └── Check the CIDR blocks (primary + any secondary CIDRs)

□ Open VPC → Subnets → filter by your VPC
  └── For each subnet, note: Name, AZ, CIDR, and whether it's public or private

□ Open VPC → Route Tables → click on each one
  └── Check the "Routes" tab: does it point to igw (public) or nat (private)?
  └── Check the "Subnet associations" tab: which subnets use this route table?

□ Open VPC → Internet Gateways → confirm yours is attached to the VPC

□ Open VPC → NAT Gateways → check you have one per AZ
  └── Note the Elastic IPs — these are the public IPs your private subnets use for outbound

□ Open VPC → Endpoints → confirm S3 and DynamoDB gateway endpoints exist
  └── Think: are there other AWS services your app calls heavily? (ECR, CloudWatch, SQS)
  └── If yes, adding interface endpoints could save NAT costs

□ Open EC2 → Security Groups → filter by your VPC
  └── For each SG, check: who can talk to what? Any 0.0.0.0/0 rules that shouldn't be there?

□ Draw the full network diagram on paper or a whiteboard. Include:
  └── VPC, subnets (public/private), AZs, IGW, NAT Gateways, VPC endpoints, route tables
```

---

## 3. Compute — EC2, Auto Scaling Groups, Launch Templates

> Updated with YOUR actual ASG from the AWS Console.

### 3.1 EC2 Instances

**Go to:** EC2 → Instances

Your company runs ECS on EC2 instances (not just Fargate). From the screenshot, you have:

```
Instance: i-05ace85b268a0d915
├── Instance Type: r6id.2xlarge
├── Lifecycle: InService
├── AZ: euw1-az2 (eu-west-1, Ireland)
├── Health: Healthy
├── Launch Template: nonprod-fusion-cicd-fusic...
└── Part of ASG: nonprod-fusion-cicd-fusion-audicom-ita-edam-doc-linux-
                 LinuxECSAutoScalingGroup-w8ONPdrmK8Xr
```

**Wait — eu-west-1 (Ireland)?** Your VPC screenshots were ap-southeast-2 (Sydney).
This means your company runs infrastructure in MULTIPLE REGIONS:

```
┌─────────────────────────────────────────────────────────────────────┐
│ Your company's multi-region setup:                                   │
│                                                                     │
│ ap-southeast-2 (Sydney):                                            │
│   VPC: ITAM-NP-VPC-AP-SOUTHEAST-2                                   │
│   Services: batch-processing-service, fusion services               │
│   ALB: internal-nonprod-fusion-shared-alb-244036836                  │
│   Likely uses: Fargate (serverless containers)                       │
│                                                                     │
│ eu-west-1 (Ireland):                                                │
│   ASG: nonprod-fusion-cicd-fusion-audicom-ita-edam-doc-linux-...     │
│   Instance: r6id.2xlarge (EC2-backed ECS)                            │
│   Services: audicom, ita, edam, doc (from the ASG name)              │
│   Uses: EC2 launch type (you manage the instances)                   │
│                                                                     │
│ This is common in large companies — different services in different  │
│ regions based on data residency, latency, or team preferences.       │
└─────────────────────────────────────────────────────────────────────┘
```

**Let's decode your instance type — r6id.2xlarge:**

```
r6id.2xlarge
│││  │
│││  └── 2xlarge = size (8 vCPUs, 64 GB RAM, 1x 474 GB NVMe SSD)
│││
││└── d = local NVMe SSD storage (fast local disk, good for temp data/caching)
││
│└── 6i = 6th generation, Intel processors
│
└── r = memory-optimized family

Why r6id for ECS?
├── 64 GB RAM = can run MANY containers on one instance
├── Local NVMe SSD = fast temp storage for container layers and logs
├── Memory-optimized = good when containers are memory-heavy
└── Cost: ~$0.6048/hr on-demand in eu-west-1 = ~$435/month per instance

Instance Type Cheat Sheet:
┌──────────┬─────────────────────────────────────────────────────────────┐
│ Family   │ Use Case                                                    │
├──────────┼─────────────────────────────────────────────────────────────┤
│ t3/t4g   │ Burstable, dev/test, low-traffic services                   │
│ m5/m6i   │ General purpose, most web applications                      │
│ c5/c6i   │ CPU-intensive (video encoding, ML inference)                │
│ r5/r6i   │ Memory-intensive (in-memory caches, DBs) ← YOUR INSTANCE   │
│ r6id     │ Memory + local NVMe SSD ← EXACTLY what you have            │
│ g4/p4    │ GPU (ML training, graphics)                                 │
│ i3       │ Storage-optimized (databases, data lakes)                   │
└──────────┴─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ Go check: EC2 → Instances → click i-05ace85b268a0d915           │
│ Look at:                                                        │
│   "Details" tab: instance type, AMI, IAM role, VPC, subnet      │
│   "Security" tab: security groups attached                      │
│   "Monitoring" tab: CPU, network, disk metrics                  │
│   "Tags" tab: look for ECS cluster name tag                     │
│                                                                 │
│ This instance is an ECS CONTAINER INSTANCE — it runs the ECS    │
│ agent and hosts your Docker containers. It's NOT a standalone   │
│ server. ECS schedules tasks (containers) onto it.               │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Launch Templates

**Go to:** EC2 → Launch Templates → find `nonprod-fusion-cicd-fusic...`

Your ASG uses this launch template to create new EC2 instances. It defines:

```
Launch Template: nonprod-fusion-cicd-fusic... (from your screenshot)
├── AMI: an ECS-optimized Amazon Linux 2 AMI
│        (pre-installed with Docker and ECS agent)
├── Instance Type: r6id.2xlarge
├── Security Groups: allows traffic from ALB, ECS service ports
├── IAM Instance Profile: role that lets the instance:
│   ├── Register with the ECS cluster
│   ├── Pull images from ECR
│   ├── Push logs to CloudWatch
│   └── Communicate with ECS control plane
├── User Data: bootstrap script that:
│   ├── Sets ECS_CLUSTER=<cluster-name> (tells the agent which cluster to join)
│   ├── Configures Docker daemon settings
│   └── May set ECS_ENABLE_TASK_IAM_ROLE=true
└── Block Device Mappings: EBS volumes + the local NVMe SSD

┌─────────────────────────────────────────────────────────────────┐
│ Go check: EC2 → Launch Templates → click on yours               │
│ Click "Versions" tab → click the latest version                 │
│ Look at "User data" — you'll see the ECS cluster config         │
│ Look at "Instance type" — confirms r6id.2xlarge                 │
└─────────────────────────────────────────────────────────────────┘
```

### 3.3 Auto Scaling Groups (ASG)

**Go to:** EC2 → Auto Scaling Groups (you already found this!)

**YOUR actual ASG:**

```
ASG Name: nonprod-fusion-cicd-fusion-audicom-ita-edam-doc-linux-
          LinuxECSAutoScalingGroup-w8ONPdrmK8Xr

Let's decode this name:
  nonprod          = non-production environment
  fusion-cicd      = deployed via fusion CI/CD pipeline
  fusion-audicom   = audicom service/team
  ita-edam-doc     = ITA, EDAM, DOC services (multiple services share this ASG)
  linux            = Linux-based instances
  LinuxECSAutoScalingGroup = this ASG backs an ECS cluster

Configuration (from your screenshot):
├── Launch Template: nonprod-fusion-cicd-fusic... (version: Latest)
├── Desired Capacity: 1 (currently running 1 instance)
├── Scaling Limits: 0 - 4 (min 0, max 4)
├── Status: Updating capacity
├── Date Created: Mon Feb 02 2026
│
├── Instance Management (1 instance):
│   └── i-05ace85b268a0d915
│       ├── Lifecycle: InService (running and healthy)
│       ├── Instance Type: r6id.2xlarge (8 vCPU, 64 GB RAM)
│       ├── AZ: euw1-az2 (eu-west-1, Ireland)
│       └── Health Status: Healthy ✅
│
└── Instance Lifecycle Policy: has lifecycle hooks configured
    (runs scripts before/after instance launch/terminate)
```

**What this means in practice:**

```
RIGHT NOW:
  1 instance (r6id.2xlarge) is running in eu-west-1.
  It has 8 vCPUs and 64 GB RAM available for ECS tasks.
  ECS can schedule multiple containers on this single instance.

  Example: if each container needs 1 vCPU + 4 GB RAM,
  this instance can run ~8 containers simultaneously.

SCALING:
  Min: 0 → ASG can scale to ZERO instances (saves cost when idle)
  Max: 4 → at peak, up to 4 instances = 32 vCPUs, 256 GB RAM

  If ECS needs to place a new task but the current instance is full:
  1. ECS tells the ASG "I need more capacity"
  2. ASG launches a new r6id.2xlarge instance (takes 2-5 minutes)
  3. New instance registers with the ECS cluster
  4. ECS places the task on the new instance

  This is SLOWER than Fargate (which launches tasks in 30-60 seconds)
  but CHEAPER for sustained workloads (EC2 pricing vs Fargate pricing).

COST:
  1 instance × r6id.2xlarge × 24/7 = ~$435/month
  4 instances (max) × 24/7 = ~$1,740/month
  With Reserved Instances or Savings Plans: 30-60% cheaper
```

**SDE-3 things to check on YOUR ASG:**

```
┌─────────────────────────────────────────────────────────────────┐
│ Go to the ASG page and check each tab:                          │
│                                                                 │
│ "Details" tab:                                                  │
│   Desired/Min/Max capacity — is min=0 intentional?              │
│   (If someone accidentally scales to 0, all tasks die)          │
│                                                                 │
│ "Automatic scaling" tab:                                        │
│   What scaling policies are configured?                         │
│   Is it using ECS Capacity Provider managed scaling?            │
│   (This lets ECS automatically tell the ASG to scale)           │
│                                                                 │
│ "Instance management" tab (you're already here):                │
│   Are all instances healthy?                                    │
│   What AZs are they in? (only euw1-az2 = single AZ risk!)      │
│                                                                 │
│ "Activity" tab:                                                 │
│   See scaling events: when did instances launch/terminate?       │
│   Look for failed launches (capacity issues, AMI problems)      │
│                                                                 │
│ "Instance refresh" tab:                                         │
│   Used for rolling updates (new AMI, new instance type)         │
│   Check if any refresh is in progress                           │
│                                                                 │
│ "Monitoring" tab:                                               │
│   Group size over time, CPU utilization, network I/O            │
│                                                                 │
│ ⚠️  CONCERN: You have 1 instance in 1 AZ (euw1-az2).           │
│ If that AZ has an issue, your ECS tasks have nowhere to run.    │
│ For production, you'd want min=2 across multiple AZs.           │
│ For nonprod with min=0, this is acceptable to save cost.        │
└─────────────────────────────────────────────────────────────────┘
```

### 3.4 ECS on EC2 vs Fargate — YOUR Company Uses Both

```
From what we've seen:

ap-southeast-2 (Sydney):
  └── Likely Fargate (serverless)
      ├── No ASG to manage
      ├── Pay per task (vCPU + memory per second)
      ├── Tasks launch in 30-60 seconds
      └── Good for: variable workloads, simplicity

eu-west-1 (Ireland):
  └── EC2 launch type (what your ASG screenshot shows)
      ├── ASG manages r6id.2xlarge instances
      ├── ECS schedules tasks onto these instances
      ├── You manage instance capacity, AMIs, patching
      ├── Cheaper for sustained workloads
      └── Good for: memory-heavy tasks, local SSD needs, cost optimization

WHY use EC2 instead of Fargate?
  ├── r6id has local NVMe SSD — Fargate doesn't offer this
  ├── 64 GB RAM per instance — Fargate max is 30 GB per task (120 GB with some configs)
  ├── Cost: running 8 containers on one r6id.2xlarge is cheaper than 8 Fargate tasks
  ├── The "d" in r6id = local disk, useful for batch processing temp files
  └── Some workloads need specific instance features Fargate doesn't support
```

### 3.5 EBS Volumes

**Go to:** EC2 → Volumes

```
EBS Volume Types:
├── gp3: General purpose SSD (most workloads, 3000 IOPS baseline, cheapest SSD)
├── io2: Provisioned IOPS SSD (databases needing consistent IOPS)
├── st1: Throughput-optimized HDD (big data, log processing)
└── sc1: Cold HDD (infrequent access, cheapest)
```

Your r6id.2xlarge also has a local NVMe SSD (474 GB) — this is ephemeral storage
(data is lost when the instance stops). Good for temp files, not for persistent data.

- Always use **gp3** over gp2 (same price, better baseline performance).
- Enable **encryption** on all volumes (use KMS).
- Set up **EBS snapshots** for backups (automate with AWS Backup).

---

## 3.6 ECS vs EC2 — What's the Difference?

These two get confused a lot because they work together. Here's the simple version:

```
EC2 = the MACHINE (a virtual server). Think of it as a laptop.
ECS = the MANAGER that runs containers ON those machines.
     Think of it as the person deciding which apps to open on the laptop.
```

**How it maps to YOUR Ireland setup:**

```
EC2 Instance: i-05ace85b268a0d915 (r6id.2xlarge)
├── This is a MACHINE — 8 vCPUs, 64 GB RAM, runs Linux
├── It exists whether or not ECS is involved
├── You could SSH into it and manually run Docker containers
├── It costs money 24/7 as long as it's running
│
└── ECS is installed on this machine (via the ECS Agent)
    ECS DECIDES what containers to run on it:
    ├── "Put audicom-service container here (2 vCPU, 8GB)"
    ├── "Put ita-service container here (1 vCPU, 4GB)"
    ├── "Put edam-service container here (1 vCPU, 4GB)"
    └── "This machine still has 4 vCPU and 48GB free, I can fit more"
```

**The hotel analogy:**

```
ECS is like a HOTEL MANAGER:
  EC2 instances = hotel rooms
  Containers    = guests
  ECS decides which guest goes in which room.
  If all rooms are full, ECS tells the ASG: "build more rooms" (launch more EC2s).
```

**ECS has two modes — and your company uses both:**

```
MODE 1: ECS on EC2 (your Ireland cluster — eu-west-1)
  YOU provide the machines (EC2 instances via ASG)
  ECS just schedules containers onto them
  You pay for EC2 instances whether containers are running or not
  You manage patching, scaling the ASG, instance types
  Your ASG: r6id.2xlarge, min 0, max 4

MODE 2: ECS on Fargate (your Sydney cluster — ap-southeast-2)
  AWS provides the machines (you never see them)
  ECS runs your container on invisible infrastructure
  You pay only when containers are running
  AWS manages everything underneath

┌─────────────────────────────────────────────────────────────┐
│                                                             │
│  ECS on EC2:     You buy the laptop, ECS runs apps on it    │
│  ECS on Fargate: You just say "run this app", AWS handles   │
│                  the laptop                                 │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

**Side-by-side comparison:**

```
┌──────────────────┬──────────────────────┬──────────────────────────────┐
│                  │ EC2 (alone, no ECS)  │ ECS (container orchestrator) │
├──────────────────┼──────────────────────┼──────────────────────────────┤
│ What is it       │ A virtual server     │ Container manager/scheduler  │
│ Runs             │ Anything (apps,      │ Docker containers only       │
│                  │  scripts, databases) │                              │
│ Scaling          │ ASG scales machines  │ ECS scales containers        │
│ Deployment       │ SSH + scripts, or    │ Task definitions, rolling    │
│                  │ CodeDeploy           │ updates, circuit breaker     │
│ You manage       │ OS, patches, Docker  │ Just your container image    │
│                  │ runtime, everything  │ and config                   │
│ Health checks    │ EC2 status checks    │ Container-level + ALB        │
│                  │ (hardware only)      │ health checks                │
│ Can they work    │ Yes, EC2 can run     │ Yes, ECS can run ON EC2      │
│ together?        │ without ECS          │ or on Fargate (no EC2)       │
│ Your setup       │ r6id.2xlarge in      │ Schedules audicom, ita,      │
│                  │ eu-west-1            │ edam, doc onto that instance │
└──────────────────┴──────────────────────┴──────────────────────────────┘
```

**How they work together in your Ireland cluster:**

```
                    ASG (manages EC2 fleet)
                    ├── Launches/terminates EC2 instances
                    ├── Min: 0, Max: 4, Desired: 1
                    │
                    ▼
              EC2 Instance (r6id.2xlarge)
              ├── 8 vCPU, 64 GB RAM
              ├── Runs Linux + Docker + ECS Agent
              │
              └── ECS Agent registers with ECS Cluster
                  │
                  ▼
              ECS Cluster (the brain)
              ├── Knows all registered EC2 instances
              ├── Knows all running tasks (containers)
              ├── Knows available capacity on each instance
              │
              ├── Service: audicom-service (desired: 2 tasks)
              │   ECS: "Place task 1 on instance i-05ace..., task 2 also fits"
              │
              ├── Service: ita-service (desired: 1 task)
              │   ECS: "Place on instance i-05ace... (still has room)"
              │
              └── Service: doc-service (desired: 1 task)
                  ECS: "Instance i-05ace... is full. Tell ASG to launch another."
                  ASG: launches i-0new... (another r6id.2xlarge)
                  ECS: "Place doc-service on the new instance."

WITHOUT ECS: you'd have to SSH into each EC2 and manually run docker commands.
WITH ECS:    you just say "I want 3 copies of audicom-service" and ECS handles it.
```

---

## 4. Containers — ECS (Elastic Container Service) End-to-End

This is the big one your colleague uses. Let's go deep.

**Key discovery: your company uses BOTH ECS launch types:**
```
ap-southeast-2 (Sydney):  Likely Fargate (serverless, no ASG visible)
eu-west-1 (Ireland):      EC2 launch type (ASG with r6id.2xlarge instances)
```

### 4.1 ECS Architecture Overview

Here's how ECS maps to YOUR setup:

```
EU-WEST-1 (Ireland) — EC2 Launch Type:
┌──────────────────────────────────────────────────────────────────────┐
│ ECS Cluster (nonprod)                                                │
│                                                                      │
│ ASG: nonprod-fusion-cicd-...-LinuxECSAutoScalingGroup                │
│ ├── Instance: i-05ace85b268a0d915 (r6id.2xlarge, 8 vCPU, 64GB RAM) │
│ │   ├── Container: audicom-service (2 vCPU, 8GB)                    │
│ │   ├── Container: ita-service (1 vCPU, 4GB)                        │
│ │   ├── Container: edam-service (1 vCPU, 4GB)                       │
│ │   ├── Container: doc-service (1 vCPU, 4GB)                        │
│ │   └── (remaining: 3 vCPU, 44GB for more containers)               │
│ │                                                                    │
│ └── (ASG can scale to 4 instances = 32 vCPU, 256GB total)           │
└──────────────────────────────────────────────────────────────────────┘

AP-SOUTHEAST-2 (Sydney) — Likely Fargate:
┌──────────────────────────────────────────────────────────────────────┐
│ ECS Cluster (nonprod)                                                │
│                                                                      │
│ ECR ──(image)──►  ┌──────────────────────┐                           │
│ (batch-processing │ Service:              │                           │
│  -service:v1.2.3) │ batch-processing-svc  │                           │
│                   │ Task x3 (Fargate)     │                           │
│ ALB ──(traffic)─► └──────────────────────┘                           │
│ /batch-processing                                                    │
│ -service/*        VPC: ITAM-NP-VPC-AP-SOUTHEAST-2                    │
│                   Subnets: PRIVATESUBNET-NONPROD-A/B                 │
└──────────────────────────────────────────────────────────────────────┘

Multiple services share ONE EC2 instance (eu-west-1).
Each Fargate task gets its OWN isolated compute (ap-southeast-2).
```
                    │                                                          │
                    │  Infrastructure: Fargate (serverless)                     │
                    │  VPC: ITAM-NP-VPC-AP-SOUTHEAST-2                         │
                    │  Subnets: PRIVATESUBNET-NONPROD-A, PRIVATESUBNET-NONPROD-B│
                    └──────────────────────────────────────────────────────────┘

  One cluster can host MANY services. Each service is a separate microservice.
  Your ALB uses path-based routing to send traffic to the right service:
    /batch-processing-service/* → batch-processing-svc target group
    /user-service/*             → user-svc target group
    /auth-service/*             → auth-svc target group
```

### 4.2 Key ECS Concepts

**Go to:** AWS Console → ECS

| Concept | What It Is |
|---------|-----------|
| **Cluster** | Logical grouping of services. Think of it as a namespace. |
| **Task Definition** | A blueprint (like docker-compose). Defines containers, CPU, memory, env vars, log config. Versioned. |
| **Task** | A running instance of a task definition. One or more containers. |
| **Service** | Manages desired count of tasks. Handles rolling deploys, load balancer registration, auto-scaling. |
| **Container Definition** | Within a task definition — one container's config (image, ports, health check, log driver). |

### 4.3 Fargate vs EC2 Launch Type — YOUR Company Uses Both

```
┌──────────────┬──────────────────────────────┬──────────────────────────────┐
│              │ Fargate                      │ EC2 (your eu-west-1 setup)   │
│              │ (your ap-southeast-2 setup)  │                              │
├──────────────┼──────────────────────────────┼──────────────────────────────┤
│ Management   │ Serverless, no instances     │ You manage ASG + instances   │
│ Pricing      │ Pay per task (vCPU + memory) │ Pay for EC2 instances        │
│ Scaling      │ Just increase task count     │ Scale ASG + tasks            │
│ Best for     │ Variable workloads           │ Sustained, memory-heavy      │
│ Networking   │ awsvpc mode (ENI per task)   │ bridge, host, or awsvpc      │
│ Storage      │ 20GB ephemeral (expandable)  │ Full EBS + local NVMe (r6id) │
│ Startup time │ 30-60 seconds                │ 2-5 min (new instance)       │
│ Instance     │ N/A                          │ r6id.2xlarge (8vCPU, 64GB)   │
│ Your ASG     │ N/A                          │ Min:0, Max:4, Desired:1      │
└──────────────┴──────────────────────────────┴──────────────────────────────┘
```

**Why your eu-west-1 cluster uses EC2 instead of Fargate:**
- `r6id` has local NVMe SSD — Fargate doesn't offer local disk
- 64 GB RAM per instance — can pack many containers efficiently
- Multiple services (audicom, ita, edam, doc) share one instance = cheaper
- Batch/processing workloads may need the local SSD for temp files

### 4.4 Task Definition Deep Dive

**Go to:** ECS → Task Definitions → find `batch-processing-service` → JSON tab

Here's what your batch-processing-service task definition likely looks like (with explanations):

```json
{
  "family": "batch-processing-service",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "512",
  "memory": "1024",
  "executionRoleArn": "arn:aws:iam::123456789:role/ecsTaskExecutionRole",
  "taskRoleArn": "arn:aws:iam::123456789:role/batch-processing-service-task-role",
  "containerDefinitions": [
    {
      "name": "batch-processing-service",
      "image": "123456789.dkr.ecr.ap-southeast-2.amazonaws.com/batch-processing-service:v1.2.3",
      "portMappings": [
        { "containerPort": 8080, "protocol": "tcp" }
      ],
      "healthCheck": {
        "command": ["CMD-SHELL", "curl -f http://localhost:8080/batch-processing-service/v1/health || exit 1"],
        "interval": 30,
        "timeout": 5,
        "retries": 3,
        "startPeriod": 60
      },
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/batch-processing-service",
          "awslogs-region": "ap-southeast-2",
          "awslogs-stream-prefix": "batch"
        }
      },
      "environment": [
        { "name": "APP_ENV", "value": "nonprod" },
        { "name": "S3_BUCKET", "value": "api-ap-southeast-2.nonprod-nielsen-iwatch.com" },
        { "name": "AWS_REGION", "value": "ap-southeast-2" }
      ],
      "secrets": [
        {
          "name": "DB_PASSWORD",
          "valueFrom": "arn:aws:secretsmanager:ap-southeast-2:123456789:secret:nonprod/batch-processing/db-password"
        }
      ],
      "essential": true
    }
  ]
}
```

**Let's break down every field — what it means for YOUR service:**

```
"family": "batch-processing-service"
  │  This is the NAME of the task definition. Each time you deploy a new
  │  version, it creates a new REVISION (e.g., batch-processing-service:1,
  │  batch-processing-service:2, ... batch-processing-service:42).
  │
  │  Go check: ECS → Task Definitions → batch-processing-service
  │  You'll see all revisions. The latest one is what's currently deployed.

"networkMode": "awsvpc"
  │  Each task gets its OWN network interface (ENI) with its own private IP.
  │  That's why in the target group you saw IPs like 10.203.107.15 — each
  │  task has a unique IP inside your PRIVATESUBNET.

"cpu": "512", "memory": "1024"
  │  512 CPU units = 0.5 vCPU
  │  1024 MB = 1 GB RAM
  │
  │  Fargate pricing (ap-southeast-2):
  │  0.5 vCPU × $0.04656/hr + 1GB × $0.00511/hr = ~$0.052/hr per task
  │  3 tasks × 24hrs × 30 days = ~$112/month for your service
  │
  │  If your batch jobs are CPU-heavy, you might need "1024" (1 vCPU)
  │  or "2048" (2 vCPU). Check CloudWatch CPU metrics to right-size.

"executionRoleArn": "...ecsTaskExecutionRole"
  │  This is ECS's own role. It needs permissions to:
  │  ├── Pull your Docker image from ECR (ecr:GetAuthorizationToken, ecr:BatchGetImage)
  │  ├── Push logs to CloudWatch (logs:CreateLogStream, logs:PutLogEvents)
  │  └── Fetch secrets from Secrets Manager (secretsmanager:GetSecretValue)
  │
  │  Think of it as: the DELIVERY GUY's permissions. He needs to pick up
  │  the container image and deliver it to Fargate. He doesn't run your code.

"taskRoleArn": "...batch-processing-service-task-role"
  │  This is YOUR APP's identity. When your code calls AWS SDK:
  │  ├── s3.get_object(Bucket="api-ap-southeast-2.nonprod-nielsen-iwatch.com", ...)
  │  ├── dynamodb.put_item(TableName="batch-jobs", ...)
  │  └── sqs.send_message(QueueUrl="...", ...)
  │
  │  It uses THIS role's permissions. If this role doesn't have s3:GetObject,
  │  your code gets "Access Denied" even though the VPC endpoint exists.
  │
  │  VPC Endpoint = the ROAD to S3
  │  Task Role    = the PERMISSION to enter S3
  │  You need BOTH.
  │
  │  ┌─────────────────────────────────────────────────────────────┐
  │  │ Go check: IAM → Roles → batch-processing-service-task-role │
  │  │ Look at the attached policies. What can your service do?    │
  │  │ Can it read S3? Write DynamoDB? Publish to SQS?             │
  │  └─────────────────────────────────────────────────────────────┘

"image": "123456789.dkr.ecr.ap-southeast-2.amazonaws.com/batch-processing-service:v1.2.3"
  │  This is the Docker image in ECR (your private Docker registry).
  │  The tag "v1.2.3" is the version. Each deployment pushes a new tag.
  │
  │  ┌─────────────────────────────────────────────────────────────┐
  │  │ Go check: ECR → Repositories → batch-processing-service    │
  │  │ You'll see all image tags (versions) that have been pushed. │
  │  │ Click on a tag → "Scan results" to see vulnerability report.│
  │  └─────────────────────────────────────────────────────────────┘

"healthCheck": { "command": ["CMD-SHELL", "curl -f http://localhost:8080/.../health || exit 1"] }
  │  Every 30 seconds, ECS runs this command INSIDE your container.
  │  If it fails 3 times in a row (after the 60s startPeriod), ECS kills
  │  the task and starts a new one.
  │
  │  startPeriod: 60 = "give the app 60 seconds to boot before checking"
  │  If your Java/Spring app takes 90 seconds to start, set this to 120.

"logConfiguration": { "logDriver": "awslogs" }
  │  All stdout/stderr from your container goes to CloudWatch Logs.
  │  Log group: /ecs/batch-processing-service
  │  Stream prefix: batch
  │
  │  ┌─────────────────────────────────────────────────────────────┐
  │  │ Go check: CloudWatch → Log Groups → /ecs/batch-processing- │
  │  │ service → click a log stream → see your app's actual logs   │
  │  └─────────────────────────────────────────────────────────────┘

"environment": [{ "name": "S3_BUCKET", "value": "api-ap-southeast-2.nonprod-nielsen-iwatch.com" }]
  │  Plain-text environment variables. Visible in the AWS Console.
  │  OK for: bucket names, region, app environment, feature flags.
  │  NOT OK for: passwords, API keys, tokens.

"secrets": [{ "name": "DB_PASSWORD", "valueFrom": "arn:aws:secretsmanager:..." }]
  │  Secrets are fetched from Secrets Manager at task startup.
  │  They appear as environment variables inside the container,
  │  but they're NOT visible in the ECS Console — only the ARN is shown.
  │  This is the secure way to pass credentials.
```

### 4.5 ECS Service Configuration

**Go to:** ECS → Clusters → find your cluster → Services → `batch-processing-service`

```
Service Configuration (what yours likely looks like):
├── Task Definition: batch-processing-service:42 (revision 42)
├── Desired Count: 3
├── Launch Type: FARGATE
├── Platform Version: 1.4.0 (LATEST)
├── Network:
│   ├── Subnets: PRIVATESUBNET-NONPROD-A, PRIVATESUBNET-NONPROD-B
│   │            (your tasks run in PRIVATE subnets, across 2+ AZs)
│   ├── Security Group: batch-processing-svc-sg
│   │            (allows inbound 8080 from ALB security group only)
│   └── Public IP: DISABLED (private subnet, no internet exposure)
├── Load Balancer:
│   ├── Target Group: batch-processing-svc-tg
│   ├── Container: batch-processing-service
│   └── Port: 8080
│   │
│   │   This is the link between ALB and your tasks.
│   │   When ALB gets a request for /batch-processing-service/*,
│   │   it forwards to this target group, which routes to your tasks.
│   │
├── Deployment Configuration:
│   ├── Type: Rolling Update
│   ├── Min Healthy: 100%  ← never go below current task count during deploy
│   ├── Max Percent: 200%  ← can temporarily double tasks during deploy
│   └── Circuit Breaker: ENABLED (with rollback)
│   │
│   │   What happens during a deploy:
│   │   1. ECS launches 3 NEW tasks with the new image (now 6 total)
│   │   2. New tasks register with target group
│   │   3. ALB health checks pass on new tasks
│   │   4. ECS drains OLD tasks (stops sending traffic, waits for in-flight)
│   │   5. ECS stops old tasks (now back to 3, all new version)
│   │
│   │   If new tasks keep failing health checks:
│   │   Circuit breaker kicks in → automatically rolls back to old version
│   │
├── Auto Scaling:
│   ├── Min: 2, Max: 20
│   ├── Target Tracking: ECSServiceAverageCPUUtilization < 60%
│   │   (if avg CPU > 60%, add more tasks. If < 60%, remove tasks.)
│   └── Target Tracking: ALBRequestCountPerTarget < 1000
│       (if each task gets > 1000 req/min, add more tasks)
└── Service Connect / Service Discovery: enabled
    │   (other microservices can find batch-processing-service
    │    via DNS like batch-processing-service.local:8080)
```

**What to check in the console right now:**
```
┌─────────────────────────────────────────────────────────────────┐
│ ECS → Clusters → your cluster → batch-processing-service        │
│                                                                 │
│ "Deployments" tab:                                              │
│   See current and previous deployments.                         │
│   PRIMARY = current running version                             │
│   ACTIVE  = new version being rolled out                        │
│                                                                 │
│ "Events" tab:                                                   │
│   See real-time events:                                         │
│   "service batch-processing-service has reached a steady state" │
│   "service batch-processing-service registered 1 targets in     │
│    target group batch-processing-svc-tg"                        │
│   "service batch-processing-service has started 1 tasks:        │
│    task abc123"                                                  │
│                                                                 │
│ "Tasks" tab:                                                    │
│   See all running tasks with their:                             │
│   - Task ID (click to see details)                              │
│   - Private IP (e.g., 10.203.107.15)                            │
│   - Last status (RUNNING, PENDING, STOPPED)                     │
│   - Health status (HEALTHY, UNHEALTHY)                          │
│   - AZ (ap-southeast-2a or 2b)                                  │
│                                                                 │
│ "Logs" tab:                                                     │
│   See recent logs from all tasks (shortcut to CloudWatch)       │
└─────────────────────────────────────────────────────────────────┘
```

### 4.6 ECR (Elastic Container Registry)

**Go to:** ECR → Repositories → find `batch-processing-service`

This is where your Docker images live. Every time your CI/CD pipeline builds a new version, it pushes here.

```bash
# What your CI/CD pipeline does behind the scenes:

# 1. Authenticate Docker to ECR (ap-southeast-2 region, matching your VPC)
aws ecr get-login-password --region ap-southeast-2 | \
  docker login --username AWS --password-stdin \
  123456789.dkr.ecr.ap-southeast-2.amazonaws.com

# 2. Build the Docker image
docker build -t batch-processing-service:v1.2.3 .

# 3. Tag it for ECR
docker tag batch-processing-service:v1.2.3 \
  123456789.dkr.ecr.ap-southeast-2.amazonaws.com/batch-processing-service:v1.2.3

# 4. Push to ECR
docker push \
  123456789.dkr.ecr.ap-southeast-2.amazonaws.com/batch-processing-service:v1.2.3

# Now ECS can pull this image when launching new tasks
```

**What to check in ECR right now:**
```
┌─────────────────────────────────────────────────────────────────┐
│ ECR → batch-processing-service repository                       │
│                                                                 │
│ "Images" tab:                                                   │
│   Tag        │ Pushed at       │ Size   │ Scan status           │
│   v1.2.3     │ 2 hours ago     │ 245MB  │ 0 critical, 2 high   │
│   v1.2.2     │ 3 days ago      │ 243MB  │ 0 critical, 1 high   │
│   v1.2.1     │ 1 week ago      │ 240MB  │ 1 critical ⚠️        │
│   latest     │ 2 hours ago     │ 245MB  │ (same as v1.2.3)     │
│                                                                 │
│ Click on "v1.2.3" → "Scan results":                            │
│   See CVEs (vulnerabilities) in your base image and deps.       │
│   Critical/High = fix before deploying to production.           │
│                                                                 │
│ "Lifecycle Policy" tab:                                         │
│   Should have rules like:                                       │
│   - Delete untagged images older than 7 days                    │
│   - Keep only last 20 tagged images                             │
│   Without this, old images pile up and cost storage fees.       │
└─────────────────────────────────────────────────────────────────┘
```

**SDE-3 things to set up:**
- **Image scanning**: enable on push (finds CVEs in your base images).
- **Lifecycle policies**: auto-delete untagged images older than 30 days, keep only last 20 tagged images. ECR storage costs add up.
- **Immutable tags**: prevent overwriting a tag (e.g., `v1.2.3` can never be re-pushed). Forces proper versioning.
- **Cross-region replication**: if you deploy to multiple regions.

### 4.7 ECS Deployment Strategies

```
┌─────────────────┬────────────────────────────────────────────────────┐
│ Strategy        │ How It Works                                       │
├─────────────────┼────────────────────────────────────────────────────┤
│ Rolling Update  │ Gradually replace old tasks with new ones.         │
│                 │ minHealthy=100%, maxPercent=200% means it launches │
│                 │ new tasks first, then drains old ones.             │
├─────────────────┼────────────────────────────────────────────────────┤
│ Blue/Green      │ Deploy new version alongside old. Switch ALB       │
│ (CodeDeploy)    │ listener from blue to green. Instant rollback.     │
├─────────────────┼────────────────────────────────────────────────────┤
│ Canary          │ Route small % of traffic to new version first.     │
│ (CodeDeploy)    │ e.g., 10% for 10 min, then 100%.                  │
└─────────────────┴────────────────────────────────────────────────────┘
```

### 4.8 ECS Exec (Debugging Running Containers)

```bash
# SSH into a running Fargate task (like docker exec)
aws ecs execute-command \
  --cluster my-cluster \
  --task arn:aws:ecs:us-east-1:123456789:task/my-cluster/abc123 \
  --container web \
  --interactive \
  --command "/bin/sh"
```

Requires: `enableExecuteCommand: true` on the service, and the task role needs SSM permissions.

---

## 5. Container Orchestration — EKS

### 5.1 When EKS Over ECS

- Your team already has Kubernetes expertise.
- You need advanced scheduling (affinity, taints, tolerations).
- You want portability across clouds.
- You need the Kubernetes ecosystem (Helm, Istio, ArgoCD, etc.).

**Go to:** EKS → Clusters (if your company uses it)

### 5.2 EKS Architecture

```
┌──────────────────────────────────────────────────┐
│                  EKS Cluster                      │
│  ┌──────────────────────────────────────────┐    │
│  │  Control Plane (AWS-managed)              │    │
│  │  API Server, etcd, scheduler, controllers │    │
│  └──────────────────────────────────────────┘    │
│                                                   │
│  ┌─────────────┐  ┌─────────────┐                │
│  │ Node Group  │  │ Fargate     │                │
│  │ (EC2-based) │  │ Profile     │                │
│  │             │  │ (serverless)│                │
│  │ m5.xlarge x3│  │             │                │
│  └─────────────┘  └─────────────┘                │
└──────────────────────────────────────────────────┘
```

### 5.3 Key EKS Concepts for SDE-3

- **Managed Node Groups**: AWS manages the EC2 instances, you define instance types and scaling.
- **Fargate Profiles**: serverless pods, no nodes to manage. Define which namespaces/labels run on Fargate.
- **IRSA (IAM Roles for Service Accounts)**: map Kubernetes service accounts to IAM roles. This is how pods get AWS permissions. Never use node-level IAM roles.
- **Cluster Autoscaler** or **Karpenter**: automatically provisions/removes nodes based on pod demand. Karpenter is newer and faster.
- **aws-load-balancer-controller**: automatically creates ALBs/NLBs from Kubernetes Ingress/Service resources.

---

## 6. Load Balancing — ALB, NLB, Target Groups

### 6.1 ALB (Application Load Balancer) — Layer 7

**Go to:** EC2 → Load Balancers

```
Internet
   │
   ▼
┌──────────────────────────────────────────────┐
│  ALB (Application Load Balancer)              │
│  Listener: 443 (HTTPS, ACM certificate)       │
│                                               │
│  Rules:                                       │
│  ├── /api/*     → Target Group: api-service   │
│  ├── /admin/*   → Target Group: admin-service │
│  └── default    → Target Group: web-service   │
└──────────────────────────────────────────────┘
         │              │              │
    ┌────▼────┐   ┌────▼────┐   ┌────▼────┐
    │ api-tg  │   │admin-tg │   │ web-tg  │
    │ ECS x3  │   │ ECS x2  │   │ ECS x4  │
    └─────────┘   └─────────┘   └─────────┘
```

Key ALB features:
- **Path-based routing**: route `/api/*` to one service, `/web/*` to another.
- **Host-based routing**: `api.example.com` → service A, `www.example.com` → service B.
- **Weighted target groups**: send 90% to v1, 10% to v2 (canary).
- **Sticky sessions**: route same user to same target (use with caution).
- **WAF integration**: attach AWS WAF for rate limiting, IP blocking, SQL injection protection.
- **Access logs**: enable and send to S3 for audit/debugging.

### 6.2 NLB (Network Load Balancer) — Layer 4

- Ultra-low latency, millions of requests per second.
- TCP/UDP/TLS passthrough.
- Static IP addresses (useful for whitelisting).
- Use for: gRPC, WebSockets, non-HTTP protocols, or when you need static IPs.

### 6.3 Target Group Health Checks

**Go to:** EC2 → Target Groups → pick one → Health checks

```
Health Check Configuration:
├── Protocol: HTTP
├── Path: /health
├── Port: traffic-port (8080)
├── Healthy threshold: 3 (consecutive successes)
├── Unhealthy threshold: 3 (consecutive failures)
├── Timeout: 5 seconds
├── Interval: 30 seconds
└── Success codes: 200
```

**SDE-3 tip:** Your `/health` endpoint should check:
- App is running (basic)
- Database connection is alive (dependency check)
- But NOT: expensive operations that could make health checks slow

Implement a tiered health check:
```
GET /health         → 200 (app is alive, for ALB)
GET /health/ready   → 200 (all dependencies ready, for deployment gates)
GET /health/deep    → 200 (full dependency check, for debugging)
```

### 6.4 Connection Draining (Deregistration Delay)

When a target is removed (during deploy or scale-in), the ALB gives it time to finish in-flight requests.

- Default: 300 seconds.
- Set it to match your longest expected request duration.
- For APIs: 30-60 seconds is usually fine.
- For WebSocket apps: set higher.

---

## 7. CI/CD Pipeline

### 7.1 The Deployment Flow

```
Developer pushes code
        │
        ▼
┌──────────────┐     ┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  Source       │────►│  Build       │────►│  Test        │────►│  Deploy      │
│  (GitHub/     │     │  (CodeBuild) │     │  (CodeBuild) │     │  (CodeDeploy │
│   CodeCommit) │     │              │     │              │     │   or ECS)    │
└──────────────┘     └──────────────┘     └──────────────┘     └──────────────┘
                            │
                            ▼
                     ┌──────────────┐
                     │  ECR         │
                     │  (push image)│
                     └──────────────┘
```

### 7.2 CodeBuild — buildspec.yml

```yaml
version: 0.2

env:
  secrets-manager:
    DOCKER_HUB_TOKEN: prod/dockerhub:token

phases:
  pre_build:
    commands:
      - echo Logging in to ECR...
      - aws ecr get-login-password --region $AWS_DEFAULT_REGION | docker login --username AWS --password-stdin $ECR_REGISTRY
      - COMMIT_HASH=$(echo $CODEBUILD_RESOLVED_SOURCE_VERSION | cut -c 1-7)
      - IMAGE_TAG=${COMMIT_HASH:-latest}

  build:
    commands:
      - echo Building Docker image...
      - docker build -t $ECR_REGISTRY/$ECR_REPO:$IMAGE_TAG .
      - docker tag $ECR_REGISTRY/$ECR_REPO:$IMAGE_TAG $ECR_REGISTRY/$ECR_REPO:latest

  post_build:
    commands:
      - echo Pushing to ECR...
      - docker push $ECR_REGISTRY/$ECR_REPO:$IMAGE_TAG
      - docker push $ECR_REGISTRY/$ECR_REPO:latest
      - echo Writing image definitions file...
      - printf '[{"name":"web","imageUri":"%s"}]' $ECR_REGISTRY/$ECR_REPO:$IMAGE_TAG > imagedefinitions.json

artifacts:
  files:
    - imagedefinitions.json

cache:
  paths:
    - '/root/.cache/pip/**/*'
    - '/root/.npm/**/*'
```

### 7.3 Deployment Safety Checklist (SDE-3 Owns This)

```
Pre-deployment:
  □ All tests pass (unit, integration, contract)
  □ Docker image scanned for vulnerabilities
  □ Database migrations are backward-compatible
  □ Feature flags in place for risky changes
  □ Rollback plan documented
  □ On-call engineer aware of deployment

During deployment:
  □ Watch error rate dashboard
  □ Watch latency p99 dashboard
  □ Watch ECS deployment events
  □ Circuit breaker enabled on ECS service

Post-deployment:
  □ Smoke tests pass
  □ Error rate hasn't increased
  □ No new exceptions in logs
  □ Canary metrics look healthy
  □ Update deployment log/changelog
```

### 7.4 GitHub Actions Alternative (Common Setup)

```yaml
# .github/workflows/deploy.yml
name: Deploy to ECS

on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    permissions:
      id-token: write  # OIDC for AWS auth
      contents: read

    steps:
      - uses: actions/checkout@v4

      - name: Configure AWS credentials (OIDC)
        uses: aws-actions/configure-aws-credentials@v4
        with:
          role-to-assume: arn:aws:iam::123456789:role/github-actions-deploy
          aws-region: us-east-1

      - name: Login to ECR
        uses: aws-actions/amazon-ecr-login@v2

      - name: Build, tag, push
        env:
          ECR_REGISTRY: 123456789.dkr.ecr.us-east-1.amazonaws.com
          IMAGE_TAG: ${{ github.sha }}
        run: |
          docker build -t $ECR_REGISTRY/my-app:$IMAGE_TAG .
          docker push $ECR_REGISTRY/my-app:$IMAGE_TAG

      - name: Deploy to ECS
        uses: aws-actions/amazon-ecs-deploy-task-definition@v1
        with:
          task-definition: task-definition.json
          service: my-service
          cluster: my-cluster
          wait-for-service-stability: true
```

**SDE-3 tip:** Use OIDC federation for GitHub Actions → AWS auth. No long-lived access keys.

---

## 8. Infrastructure as Code

### 8.1 CloudFormation vs Terraform

```
┌──────────────────┬──────────────────────────┬──────────────────────────┐
│                  │ CloudFormation            │ Terraform                │
├──────────────────┼──────────────────────────┼──────────────────────────┤
│ Language         │ YAML/JSON                │ HCL                      │
│ State            │ Managed by AWS           │ You manage (S3 + Dynamo) │
│ Multi-cloud      │ AWS only                 │ Any provider             │
│ Drift detection  │ Built-in                 │ terraform plan           │
│ Modules          │ Nested stacks            │ Modules (more flexible)  │
│ Rollback         │ Automatic on failure     │ Manual                   │
│ Learning curve   │ Lower for AWS-only       │ Higher but more powerful │
└──────────────────┴──────────────────────────┴──────────────────────────┘
```

### 8.2 Terraform Example — ECS Service

```hcl
# ECS Cluster
resource "aws_ecs_cluster" "main" {
  name = "production"

  setting {
    name  = "containerInsights"
    value = "enabled"
  }
}

# ECS Service
resource "aws_ecs_service" "web" {
  name            = "web-service"
  cluster         = aws_ecs_cluster.main.id
  task_definition = aws_ecs_task_definition.web.arn
  desired_count   = 3
  launch_type     = "FARGATE"

  network_configuration {
    subnets          = var.private_subnet_ids
    security_groups  = [aws_security_group.app.id]
    assign_public_ip = false
  }

  load_balancer {
    target_group_arn = aws_lb_target_group.web.arn
    container_name   = "web"
    container_port   = 8080
  }

  deployment_circuit_breaker {
    enable   = true
    rollback = true
  }

  deployment_maximum_percent         = 200
  deployment_minimum_healthy_percent = 100
}
```

### 8.3 Terraform State Management

```hcl
# backend.tf — ALWAYS use remote state
terraform {
  backend "s3" {
    bucket         = "my-company-terraform-state"
    key            = "production/web-service/terraform.tfstate"
    region         = "us-east-1"
    dynamodb_table = "terraform-locks"  # prevents concurrent modifications
    encrypt        = true
  }
}
```

**SDE-3 responsibility:**
- State file contains secrets. S3 bucket must be encrypted, versioned, and access-logged.
- Use workspaces or separate state files per environment (dev/staging/prod).
- Run `terraform plan` in CI, `terraform apply` only after approval.
- Use `terraform import` for existing resources, never recreate what's already running.

---

## 9. Storage — S3 Deep Dive

### 9.1 S3 as More Than File Storage

**Go to:** S3 → browse your company's buckets

S3 is used for everything:
- Static asset hosting (images, JS, CSS)
- Application data storage
- Log storage (ALB logs, CloudTrail, VPC Flow Logs)
- Data lake (analytics, ML training data)
- Terraform state
- Backup storage
- CI/CD artifacts

### 9.2 Storage Classes & Cost Optimization

```
┌─────────────────────┬──────────┬───────────────────────────────────────┐
│ Storage Class        │ Cost/GB  │ Use Case                              │
├─────────────────────┼──────────┼───────────────────────────────────────┤
│ S3 Standard          │ $0.023   │ Frequently accessed data              │
│ S3 Intelligent-Tier  │ $0.023*  │ Unknown/changing access patterns      │
│ S3 Standard-IA       │ $0.0125  │ Infrequent access, rapid retrieval    │
│ S3 One Zone-IA       │ $0.01    │ Infrequent, non-critical, single AZ   │
│ S3 Glacier Instant   │ $0.004   │ Archive, millisecond retrieval        │
│ S3 Glacier Flexible  │ $0.0036  │ Archive, minutes-hours retrieval      │
│ S3 Glacier Deep      │ $0.00099 │ Long-term archive, 12-48hr retrieval  │
└─────────────────────┴──────────┴───────────────────────────────────────┘
* Intelligent-Tiering has a small monitoring fee per object
```

### 9.3 Lifecycle Policies

**Go to:** S3 → bucket → Management → Lifecycle rules

```json
{
  "Rules": [
    {
      "ID": "archive-old-logs",
      "Filter": { "Prefix": "logs/" },
      "Transitions": [
        { "Days": 30, "StorageClass": "STANDARD_IA" },
        { "Days": 90, "StorageClass": "GLACIER_IR" },
        { "Days": 365, "StorageClass": "DEEP_ARCHIVE" }
      ],
      "Expiration": { "Days": 2555 },
      "Status": "Enabled"
    }
  ]
}
```

### 9.4 S3 for Caching (Static Assets via CloudFront)

```
User Request
     │
     ▼
┌──────────┐  cache hit   ┌──────────┐
│CloudFront│◄────────────►│  Edge    │ → return cached response (fast)
│          │  cache miss   │ Location │
└────┬─────┘              └──────────┘
     │
     ▼ (origin fetch)
┌──────────┐
│  S3      │
│  Bucket  │
└──────────┘
```

**S3 + CloudFront best practices:**
- Use **Origin Access Control (OAC)** — S3 bucket is private, only CloudFront can access it.
- Set **Cache-Control headers** on S3 objects: `max-age=31536000, immutable` for hashed assets.
- Use **invalidation** sparingly (costs money). Better to use content-hashed filenames.
- Enable **S3 Transfer Acceleration** for uploads from distant regions.

### 9.5 S3 Security

```
Bucket Policy (resource-based):
├── Deny all non-HTTPS requests
├── Deny all non-encrypted uploads
├── Allow only specific IAM roles
└── Deny public access (Block Public Access = ON)

Object-level:
├── Server-side encryption: SSE-S3 or SSE-KMS
├── Object Lock: WORM compliance (write once, read many)
└── Versioning: enabled (protects against accidental deletes)
```

**SDE-3 must-do:** Enable S3 Block Public Access at the account level. One misconfigured bucket policy can leak your entire data.

### 9.6 S3 Performance

- **Multipart upload**: for files > 100MB. Parallel uploads of parts.
- **S3 Select**: query CSV/JSON/Parquet files in-place without downloading. Saves bandwidth.
- **Prefix design**: S3 scales per-prefix (3,500 PUT/s, 5,500 GET/s per prefix). Spread objects across prefixes for high throughput.

---

## 10. Caching — ElastiCache, CloudFront

### 10.1 ElastiCache (Redis)

**Go to:** ElastiCache → Redis clusters

```
Application
     │
     ├──► Redis (ElastiCache)  ← cache hit (sub-ms latency)
     │         │
     │    cache miss
     │         │
     └──► Database (RDS)       ← slower (5-50ms)
```

**Common caching patterns:**

```python
# Cache-Aside (Lazy Loading) — most common
def get_user(user_id):
    # 1. Check cache
    cached = redis.get(f"user:{user_id}")
    if cached:
        return json.loads(cached)

    # 2. Cache miss — fetch from DB
    user = db.query("SELECT * FROM users WHERE id = %s", user_id)

    # 3. Populate cache with TTL
    redis.setex(f"user:{user_id}", 3600, json.dumps(user))
    return user

# Write-Through — update cache on every write
def update_user(user_id, data):
    db.execute("UPDATE users SET ... WHERE id = %s", user_id)
    redis.setex(f"user:{user_id}", 3600, json.dumps(data))
```

**ElastiCache Redis configuration:**
- **Cluster mode disabled**: single shard, up to 5 read replicas. Simple. Good for most apps.
- **Cluster mode enabled**: multiple shards, data partitioned. For large datasets (>100GB) or high write throughput.
- **Multi-AZ with auto-failover**: always enable in production.
- **Encryption in-transit and at-rest**: always enable.

### 10.2 CloudFront (CDN)

**Go to:** CloudFront → Distributions

CloudFront caches content at 400+ edge locations worldwide.

**Use for:**
- Static assets (S3 origin)
- API responses (ALB origin, with short TTLs)
- WebSocket connections
- Lambda@Edge for request/response manipulation

**Cache behaviors:**
```
Distribution: d1234.cloudfront.net
├── /static/*  → S3 origin, TTL: 1 year, compress: yes
├── /api/*     → ALB origin, TTL: 0 (pass-through), forward cookies/headers
└── default    → S3 origin (SPA index.html), TTL: 5 min
```

---

## 11. Databases — RDS, DynamoDB, Aurora

### 11.1 RDS (Relational Database Service)

**Go to:** RDS → Databases

```
RDS Production Checklist:
├── Multi-AZ: ENABLED (synchronous standby in another AZ)
├── Read Replicas: 1-5 (async, for read-heavy workloads)
├── Storage: gp3 (or io2 for high IOPS)
├── Encryption: ENABLED (KMS)
├── Automated Backups: 7-35 day retention
├── Performance Insights: ENABLED (free tier available)
├── Enhanced Monitoring: ENABLED (OS-level metrics)
├── Parameter Group: custom (not default)
├── Deletion Protection: ENABLED
├── Minor Version Auto-Upgrade: ENABLED
└── Maintenance Window: off-peak hours
```

**SDE-3 database responsibilities:**
- **Connection pooling**: use PgBouncer or RDS Proxy. Don't let your app open 500 direct connections.
- **Slow query logging**: enable and monitor. Queries > 1s need attention.
- **Index management**: review `pg_stat_user_indexes` for unused indexes, `pg_stat_user_tables` for sequential scans.
- **Migration safety**: always backward-compatible. Add column → deploy code → backfill → add constraint. Never drop columns in the same deploy.

### 11.2 Aurora

Aurora is AWS's cloud-native relational DB (MySQL/PostgreSQL compatible):
- 5x throughput of standard MySQL, 3x of PostgreSQL.
- Storage auto-scales up to 128TB.
- 6 copies of data across 3 AZs.
- **Aurora Serverless v2**: scales compute automatically (good for variable workloads).
- **Global Database**: cross-region replication with <1s lag.

### 11.3 DynamoDB

**Go to:** DynamoDB → Tables

```
DynamoDB Key Concepts:
├── Partition Key (PK): how data is distributed
├── Sort Key (SK): how data is ordered within a partition
├── GSI (Global Secondary Index): alternate query patterns
├── LSI (Local Secondary Index): alternate sort within same PK
├── Capacity Modes:
│   ├── On-Demand: pay per request (unpredictable traffic)
│   └── Provisioned: set RCU/WCU (predictable, cheaper)
└── DynamoDB Streams: CDC (change data capture) for event-driven
```

**Single-table design** (advanced DynamoDB pattern):
```
┌──────────────┬──────────────┬──────────────────────────┐
│ PK           │ SK           │ Data                     │
├──────────────┼──────────────┼──────────────────────────┤
│ USER#123     │ PROFILE      │ {name, email, ...}       │
│ USER#123     │ ORDER#001    │ {total, status, ...}     │
│ USER#123     │ ORDER#002    │ {total, status, ...}     │
│ ORDER#001    │ ITEM#A       │ {product, qty, ...}      │
│ ORDER#001    │ ITEM#B       │ {product, qty, ...}      │
└──────────────┴──────────────┴──────────────────────────┘

Query: Get all orders for user 123
  PK = "USER#123" AND SK begins_with("ORDER#")
```

### 11.4 Database Caching Strategy

```
Request Flow:
1. Check Redis cache (sub-ms)
2. Cache miss → Check DynamoDB DAX (single-digit ms) [if using DynamoDB]
3. Cache miss → Query database (5-50ms)
4. Populate cache with appropriate TTL

TTL Guidelines:
├── User session data: 30 min
├── User profile: 1 hour
├── Product catalog: 5 min
├── Configuration: 10 min
├── Search results: 1 min
└── Real-time data (stock prices): no cache
```

---

## 12. Messaging & Async — SQS, SNS, EventBridge

### 12.1 SQS (Simple Queue Service)

**Go to:** SQS → Queues

```
Producer ──► SQS Queue ──► Consumer (ECS task / Lambda)
                │
                └──► Dead Letter Queue (DLQ) ← failed messages after N retries
```

**Standard vs FIFO:**
```
┌──────────────┬─────────────────────────┬─────────────────────────┐
│              │ Standard                │ FIFO                    │
├──────────────┼─────────────────────────┼─────────────────────────┤
│ Throughput   │ Unlimited               │ 3,000 msg/s (batching)  │
│ Ordering     │ Best-effort             │ Guaranteed              │
│ Delivery     │ At-least-once           │ Exactly-once            │
│ Use case     │ Most workloads          │ Order-sensitive ops     │
└──────────────┴─────────────────────────┴─────────────────────────┘
```

**SDE-3 SQS best practices:**
- Always configure a **Dead Letter Queue (DLQ)** with `maxReceiveCount: 3-5`.
- Set **visibility timeout** > your processing time (default 30s, increase for long tasks).
- Use **long polling** (`WaitTimeSeconds: 20`) to reduce empty receives and cost.
- Monitor `ApproximateAgeOfOldestMessage` — if it's growing, your consumers can't keep up.
- Implement **idempotent consumers** — messages can be delivered more than once.

### 12.2 SNS (Simple Notification Service)

```
Publisher ──► SNS Topic ──► SQS Queue (fan-out)
                       ──► Lambda
                       ──► HTTP endpoint
                       ──► Email
                       ──► SMS
```

**Fan-out pattern** (very common):
```
Order Service publishes "order.created" to SNS
    ├──► SQS: inventory-queue → Inventory Service (decrement stock)
    ├──► SQS: email-queue → Email Service (send confirmation)
    ├──► SQS: analytics-queue → Analytics Service (track metrics)
    └──► Lambda: audit-function (write to audit log)
```

### 12.3 EventBridge

EventBridge is the evolution of SNS for event-driven architectures.

```
Event Sources                    EventBridge                    Targets
├── AWS Services ──────────►    ┌──────────┐    ──────────► Lambda
├── Custom Apps  ──────────►    │  Event   │    ──────────► SQS
├── SaaS Partners ─────────►    │  Bus     │    ──────────► Step Functions
                                │          │    ──────────► ECS Task
                                │  Rules:  │    ──────────► API Gateway
                                │  filter  │    ──────────► SNS
                                │  & route │
                                └──────────┘
```

**EventBridge vs SNS:**
- EventBridge: content-based filtering (filter on any field in the event JSON), schema registry, replay, archive.
- SNS: simpler, higher throughput, message filtering on attributes only.

---

## 13. Secrets & Configuration

### 13.1 Secrets Manager

**Go to:** Secrets Manager

```
Use Secrets Manager for:
├── Database credentials (with auto-rotation)
├── API keys
├── OAuth tokens
├── TLS certificates
└── Any sensitive configuration

DO NOT store secrets in:
├── Environment variables in task definitions (visible in console)
├── Code repositories
├── S3 (unless encrypted + tightly scoped)
├── Parameter Store (for actual secrets — use Secrets Manager)
└── Docker images
```

**Auto-rotation** (critical for SDE-3):
```
Secrets Manager can automatically rotate DB passwords:
1. Lambda rotation function creates new password
2. Updates the secret in Secrets Manager
3. Updates the password in RDS
4. Your app fetches the new secret on next read (or cache refresh)

Rotation schedule: every 30-90 days
```

### 13.2 Systems Manager Parameter Store

```
Use Parameter Store for:
├── Application configuration (non-secret)
├── Feature flags
├── Endpoint URLs
├── Environment-specific config
└── Shared configuration across services

Hierarchy:
/production/web-service/database-url
/production/web-service/feature-flags/new-checkout
/staging/web-service/database-url
```

**Parameter Store vs Secrets Manager:**
- Parameter Store: free (standard tier), simple key-value, no rotation.
- Secrets Manager: $0.40/secret/month, auto-rotation, cross-account sharing, versioning.

### 13.3 KMS (Key Management Service)

**Go to:** KMS → Customer managed keys

KMS manages encryption keys for everything:
- S3 encryption (SSE-KMS)
- EBS volume encryption
- RDS encryption
- Secrets Manager encryption
- DynamoDB encryption

**SDE-3 tip:** Use **customer-managed keys (CMK)** over AWS-managed keys when you need:
- Key rotation control
- Cross-account access
- Fine-grained key policies
- Audit trail of key usage (CloudTrail)

---

## 14. IAM Deep Dive

### 14.1 IAM Principals

```
IAM Entities:
├── Users: human identities (minimize these, use SSO)
├── Groups: collection of users with shared policies
├── Roles: assumed by services, applications, cross-account
│   ├── EC2 Instance Role
│   ├── ECS Task Role
│   ├── Lambda Execution Role
│   ├── Cross-Account Role
│   └── GitHub Actions OIDC Role
└── Policies: JSON documents defining permissions
```

### 14.2 Policy Anatomy

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "AllowS3ReadSpecificBucket",
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::my-app-data",
        "arn:aws:s3:::my-app-data/*"
      ],
      "Condition": {
        "StringEquals": {
          "aws:RequestedRegion": "us-east-1"
        }
      }
    }
  ]
}
```

### 14.3 Least Privilege Principle (SDE-3 Must Enforce)

```
BAD (too broad):
  "Action": "s3:*"
  "Resource": "*"

GOOD (specific):
  "Action": ["s3:GetObject", "s3:PutObject"]
  "Resource": "arn:aws:s3:::my-specific-bucket/my-prefix/*"

BETTER (with conditions):
  + Condition: only from specific VPC endpoint
  + Condition: only with encryption
  + Condition: only specific IP range
```

**Tools to help:**
- **IAM Access Analyzer**: finds resources shared externally, generates least-privilege policies from CloudTrail.
- **IAM Policy Simulator**: test policies before applying.
- **CloudTrail + Athena**: query actual API calls to see what permissions are really used.

### 14.4 Cross-Account Access

```
Account A (Production)                Account B (CI/CD)
┌─────────────────────┐              ┌─────────────────────┐
│ Role: deploy-role   │◄─── assume ──│ GitHub Actions       │
│ Trust: Account B    │   role       │ (OIDC provider)      │
│ Permissions:        │              │                      │
│   ECS deploy        │              │                      │
│   ECR push          │              │                      │
└─────────────────────┘              └─────────────────────┘
```

---

## 15. Logging

### 15.1 Structured Logging (SDE-3 Standard)

**Stop doing this:**
```
logger.info("User logged in: john")
logger.error("Failed to process order")
```

**Start doing this:**
```python
import structlog

logger = structlog.get_logger()

logger.info("user.login",
    user_id="usr_123",
    ip_address="[redacted]",
    auth_method="oauth",
    duration_ms=45
)

logger.error("order.processing_failed",
    order_id="ord_456",
    error_type="payment_declined",
    retry_count=2,
    correlation_id="req_789"
)
```

**Output (JSON):**
```json
{
  "event": "user.login",
  "user_id": "usr_123",
  "auth_method": "oauth",
  "duration_ms": 45,
  "timestamp": "2025-01-15T10:30:00Z",
  "level": "info",
  "service": "auth-service",
  "environment": "production",
  "trace_id": "abc123def456"
}
```

### 15.2 CloudWatch Logs

**Go to:** CloudWatch → Log groups

```
Log Group Hierarchy:
/ecs/production/web-service        ← ECS task logs
/ecs/production/worker-service
/aws/lambda/my-function            ← Lambda logs
/aws/rds/cluster/my-db/postgresql  ← RDS logs
/aws/alb/my-load-balancer          ← ALB access logs (usually in S3)
/custom/my-application             ← custom application logs
```

### 15.3 CloudWatch Log Insights (Powerful Query Language)

**Go to:** CloudWatch → Logs → Log Insights

```sql
-- Find all errors in the last hour
fields @timestamp, @message, @logStream
| filter @message like /ERROR/
| sort @timestamp desc
| limit 100

-- Count errors by type
fields @timestamp, @message
| filter @message like /ERROR/
| parse @message '"error_type":"*"' as error_type
| stats count(*) as error_count by error_type
| sort error_count desc

-- P99 latency by endpoint (structured logs)
fields @timestamp, endpoint, duration_ms
| filter ispresent(duration_ms)
| stats percentile(duration_ms, 99) as p99,
        percentile(duration_ms, 95) as p95,
        avg(duration_ms) as avg_ms
  by endpoint
| sort p99 desc

-- Find slow requests
fields @timestamp, endpoint, duration_ms, user_id, trace_id
| filter duration_ms > 5000
| sort duration_ms desc
| limit 50
```

### 15.4 Log Retention & Cost

```
Log Retention Strategy:
├── Production application logs: 30 days in CloudWatch, then S3
├── Security/audit logs: 90 days in CloudWatch, 7 years in S3 Glacier
├── Debug logs: 7 days (or don't ship to CloudWatch at all)
├── ALB access logs: S3 directly, 90 days Standard, then Glacier
└── VPC Flow Logs: S3, 30 days (enable only when debugging)
```

**Cost tip:** CloudWatch Logs ingestion is $0.50/GB. A chatty service logging 10GB/day = $150/month just for logs. Use log levels wisely. Don't log request/response bodies in production unless debugging.

### 15.5 EC2 Console Applications (.exe) — Logs Without RDP

You have a .exe console application running on an EC2 instance (Windows).
Right now you RDP in to check logs. Here's how to get those logs into
CloudWatch and Grafana instead.

**The setup:**

```
WITHOUT CloudWatch Agent (painful):
  You → RDP into EC2 → open log file / Event Viewer → read manually
  ❌ Can't search across time ranges
  ❌ Can't set alerts
  ❌ Can't correlate with other services
  ❌ Need RDP access (security risk)
  ❌ If the instance dies, logs are gone

WITH CloudWatch Agent (what you want):
  .exe writes to log file (e.g., C:\App\logs\app.log)
       │
       ▼
  CloudWatch Agent (runs on the EC2, reads the log file)
       │
       ▼
  CloudWatch Logs (log group: /ec2/my-console-app)
       │
       ├──► CloudWatch Log Insights (search & query)
       ├──► CloudWatch Alarms (alert on errors)
       └──► Grafana (dashboards, Loki-style log browsing)

  ✅ Search logs from your browser
  ✅ Set alerts on error patterns
  ✅ Logs survive instance termination
  ✅ No RDP needed
  ✅ Correlate with metrics from other services
```

**Step 1: Check if CloudWatch Agent is already installed**

```
RDP into the instance (one last time!) and check:

  Option A — Check Windows Services:
    Open Services (services.msc)
    Look for "Amazon CloudWatch Agent" or "AmazonCloudWatchAgent"
    If it exists and is Running → agent is already installed! Skip to Step 3.

  Option B — Check from command line:
    "C:\Program Files\Amazon\AmazonCloudWatchAgent\amazon-cloudwatch-agent-ctl.cmd" ^
      -a status
    If it returns "running" → already installed.

  Option C — Use Systems Manager (no RDP needed):
    Go to: AWS Console → Systems Manager → Run Command
    Run: AWS-RunPowerShellScript
    Command: Get-Service AmazonCloudWatchAgent
    Target: your instance ID
    → This runs the command remotely without RDP!
```

**Step 2: Install CloudWatch Agent (if not installed)**

```
Best way — via Systems Manager (no RDP):

  Go to: Systems Manager → Run Command → Run a command
  Document: AWS-ConfigureAWSPackage
  Action: Install
  Name: AmazonCloudWatchAgent
  Target: select your instance

  This installs the agent remotely. No RDP needed.

  The instance needs an IAM role with these policies:
  ├── CloudWatchAgentServerPolicy (to push logs and metrics)
  └── AmazonSSMManagedInstanceCore (for Systems Manager access)

  ┌─────────────────────────────────────────────────────────────┐
  │ Go check: EC2 → Instances → your instance → Security tab    │
  │ Look at the IAM Role. Click on it.                          │
  │ Check if CloudWatchAgentServerPolicy is attached.           │
  │ If not, attach it (or ask your infra team to).              │
  └─────────────────────────────────────────────────────────────┘
```

**Step 3: What your CloudWatch Agent is ACTUALLY collecting (from your real config)**

Your CloudWatch Agent is already installed and running on instance `i-00acc0413ebfc71f1`
at `C:\ProgramData\Amazon\AmazonCloudWatchAgent\`.

The config is managed via **SSM (Systems Manager)** — the source of truth is:
`Configs\ssm__nonprod_fusion_cloudwatch-agent_windows_common`

The agent compiles this into `amazon-cloudwatch-agent.toml` which is what actually runs.

**Here's EVERYTHING your agent is collecting:**

```
FILE LOGS — 10 files shipped to 10 CloudWatch log groups:
┌──────────────────────────────────────────┬──────────────────────────────────────────────────┐
│ File on EC2 (what agent reads)           │ CloudWatch Log Group (where it goes)              │
├──────────────────────────────────────────┼──────────────────────────────────────────────────┤
│ c:\s3sync\log\s3sync.log                │ /nonprod/fusion/ec2/s3sync.log                    │
│ c:\s3sync\log\s3sync.stderr.log         │ /nonprod/fusion/ec2/s3sync.stderr.log             │
│ c:\cfn\log\cfn-init.log                 │ /nonprod/fusion/ec2/cfn-init.log                  │
│ c:\cfn\log\cfn-init-cmd.log             │ /nonprod/fusion/ec2/cfn-init-cmd.log              │
│ c:\cfn\log\cfn-wire.log                 │ /nonprod/fusion/ec2/cfn-wire.log                  │
│ c:\cfn\log\healthcheck.log              │ /nonprod/fusion/ec2/healthcheck.log               │
│ c:\cfn\log\healthcheck.stderr.log       │ /nonprod/fusion/ec2/healthcheck.stderr.log        │
│ c:\inetpub\logs\LogFiles\W3SVC1\u_*.log │ /nonprod/fusion/ec2/w3svc1/default.log            │
│ c:\FusionUpdateServer\Temp\*.log        │ /nonprod/fusion/ec2/fusion-update-server.log      │
│ C:\ProgramData\Amazon\SSM\Logs\*.log    │ /nonprod/fusion/ec2/ssm.log                       │
└──────────────────────────────────────────┴──────────────────────────────────────────────────┘

WINDOWS EVENT LOGS — 4 event channels (WARNING, ERROR, CRITICAL only):
┌──────────────────────────────────────────┬──────────────────────────────────────────────────┐
│ Windows Event Channel                    │ CloudWatch Log Group                              │
├──────────────────────────────────────────┼──────────────────────────────────────────────────┤
│ System                                   │ /nonprod/fusion/ec2/windows_events/system          │
│ Security                                 │ /nonprod/fusion/ec2/windows_events/security        │
│ Application                              │ /nonprod/fusion/ec2/windows_events/application  ✅ │
│ CrowdStrike Falcon Sensor                │ /nonprod/fusion/ec2/windows_events/crowd_strike    │
└──────────────────────────────────────────┴──────────────────────────────────────────────────┘

METRICS — sent to CloudWatch Metrics (namespace: CWAgent):
├── Processor: % Processor Time (CPU usage)
├── LogicalDisk: % Free Space (disk usage per drive)
├── Memory: % Committed Bytes In Use (RAM usage)
├── Network Interface: Bytes Received/sec, Bytes Sent/sec
└── StatsD listener on port 8125 (apps can push custom metrics here)
```

**⚠️ KEY FINDING: Your .exe console app logs are NOT being collected.**

All 10 file paths are infrastructure logs (S3 sync, CloudFormation, IIS, health checks).
None of them point to your console app's log file.

**BUT — there are TWO ways your .exe logs might ALREADY be in CloudWatch:**

```
WAY 1: Windows Application Event Log (likely already working!)

  If your .exe is a .NET app, it probably writes to the Windows Event Log
  under the "Application" channel. Many .NET apps do this by default
  (especially if using EventLog, ILogger, or crash dumps).

  The agent IS collecting Application events (WARNING, ERROR, CRITICAL).
  These go to: /nonprod/fusion/ec2/windows_events/application

  ┌─────────────────────────────────────────────────────────────────┐
  │ GO CHECK RIGHT NOW:                                             │
  │                                                                 │
  │ CloudWatch → Log Groups →                                       │
  │   /nonprod/fusion/ec2/windows_events/application                │
  │                                                                 │
  │ Click on log stream: i-00acc0413ebfc71f1                        │
  │ Search for your app name or any error messages you recognize.   │
  │                                                                 │
  │ If your .exe crashes or logs errors, they'll be here as XML.    │
  └─────────────────────────────────────────────────────────────────┘

WAY 2: IIS Logs (if your .exe is behind IIS)

  The agent collects: c:\inetpub\logs\LogFiles\W3SVC1\u_*.log
  These go to: /nonprod/fusion/ec2/w3svc1/default.log

  If your console app is hosted behind IIS (as a Windows Service or
  via IIS reverse proxy), the HTTP request logs will be here.

  ┌─────────────────────────────────────────────────────────────────┐
  │ GO CHECK:                                                       │
  │ CloudWatch → Log Groups →                                       │
  │   /nonprod/fusion/ec2/w3svc1/default.log                       │
  │                                                                 │
  │ These are IIS access logs (like ALB access logs):               │
  │ timestamp, client IP, HTTP method, URL, status code, duration   │
  └─────────────────────────────────────────────────────────────────┘
```

**All 14 log groups you can check in CloudWatch right now (no RDP needed):**

```
┌─────────────────────────────────────────────────────────────────┐
│ Go to: CloudWatch → Log Groups → search "/nonprod/fusion/ec2"   │
│                                                                 │
│ You'll find these log groups:                                   │
│                                                                 │
│ FILE LOGS:                                                      │
│   /nonprod/fusion/ec2/s3sync.log              ← S3 sync ops    │
│   /nonprod/fusion/ec2/s3sync.stderr.log       ← S3 sync errors │
│   /nonprod/fusion/ec2/cfn-init.log            ← CF bootstrap   │
│   /nonprod/fusion/ec2/cfn-init-cmd.log        ← CF commands    │
│   /nonprod/fusion/ec2/cfn-wire.log            ← CF API calls   │
│   /nonprod/fusion/ec2/healthcheck.log         ← health checks  │
│   /nonprod/fusion/ec2/healthcheck.stderr.log  ← HC errors      │
│   /nonprod/fusion/ec2/w3svc1/default.log      ← IIS web logs   │
│   /nonprod/fusion/ec2/fusion-update-server.log← deploy/updates │
│   /nonprod/fusion/ec2/ssm.log                 ← SSM agent      │
│                                                                 │
│ WINDOWS EVENTS:                                                 │
│   /nonprod/fusion/ec2/windows_events/system      ← OS events   │
│   /nonprod/fusion/ec2/windows_events/security     ← security   │
│   /nonprod/fusion/ec2/windows_events/application  ← APP LOGS ✅│
│   /nonprod/fusion/ec2/windows_events/crowd_strike ← antivirus  │
│                                                                 │
│ Click any log group → click stream "i-00acc0413ebfc71f1"        │
│ → see the actual logs from your EC2 instance.                   │
│                                                                 │
│ START WITH: /nonprod/fusion/ec2/windows_events/application      │
│ Your .exe errors are most likely here.                          │
└─────────────────────────────────────────────────────────────────┘
```

**METRICS you can see in CloudWatch (also no RDP needed):**

```
Go to: CloudWatch → Metrics → CWAgent namespace

You'll find:
├── CPU: Processor → % Processor Time → instance i-00acc0413ebfc71f1
├── RAM: Memory → % Committed Bytes In Use
├── Disk: LogicalDisk → % Free Space (per drive: C:, D:, etc.)
└── Network: Network Interface → Bytes Received/sec, Bytes Sent/sec

These are the same metrics you'd see in Task Manager on the RDP session,
but now available in CloudWatch dashboards and Grafana.
```

**If your .exe writes to its OWN log file (not Windows Event Log):**

```
You need to find where your .exe writes logs. On the RDP session, check:
  - The folder where the .exe is located (look for *.log, *.txt files)
  - C:\ProgramData\YourAppName\
  - C:\Users\Administrator\AppData\Local\YourAppName\
  - Check the .exe's config file (app.config, appsettings.json) for log paths

Once you find the path (e.g., C:\MyApp\logs\app.log), you need to add it
to the CloudWatch Agent config. BUT — this config is managed via SSM:

  Config source: Configs\ssm__nonprod_fusion_cloudwatch-agent_windows_common
  This was pushed from: SSM Parameter Store

  ⚠️  DON'T edit the local files — SSM will overwrite your changes.

  Instead, ask your infra/DevOps team to:
  1. Update the SSM Parameter: nonprod_fusion_cloudwatch-agent_windows_common
  2. Add your .exe's log file path to the collect_list
  3. Re-run the SSM document to push the new config

  Or if you have SSM access yourself:
  Go to: Systems Manager → Parameter Store
  Search for: nonprod_fusion_cloudwatch-agent_windows_common
  Edit the JSON → add your log file entry → save
  Then: Run Command → AmazonCloudWatch-ManageAgent → configure
```

**Example: adding your .exe log file to the config:**

```json
// Add this to the "collect_list" array in the SSM parameter:
{
  "file_path": "C:\\YourApp\\logs\\*.log",
  "log_group_name": "/nonprod/fusion/ec2/your-console-app",
  "log_stream_name": "{instance_id}"
}
```

**StatsD — push custom metrics from your .exe (no config change needed!):**

```
The agent already listens on port 8125 for StatsD metrics.
Your .exe can push custom metrics directly:

  C# example:
  using var udpClient = new UdpClient();
  var data = Encoding.ASCII.GetBytes("my_app.request_count:1|c");
  udpClient.Send(data, data.Length, "localhost", 8125);

  // Metric types:
  // counter:   "my_app.errors:1|c"
  // gauge:     "my_app.queue_depth:42|g"
  // timer:     "my_app.response_time:250|ms"

  These show up in CloudWatch → Metrics → CWAgent namespace.
  No config change needed — the StatsD receiver is already running.
```

**Instance detail from your config:**

```
Instance ID: i-00acc0413ebfc71f1
Private IP:  10.203.107.242 (from your RDP title bar)
Subnet:      10.203.107.0/24 → PRIVATESUBNET-NONPROD-A (ap-southeast-2a)
VPC:         ITAM-NP-VPC-AP-SOUTHEAST-2
Region:      ap-southeast-2 (Sydney)

This instance is in the SAME private subnet as your ECS tasks.
It can talk to the internal ALB, S3 (via VPC endpoint), DynamoDB directly.
You RDP via VPN since it has no public IP.
```

**Step 4: If your .exe writes to stdout (console) instead of a file**

```
Many console apps write to stdout (the console window).
CloudWatch Agent reads FILES, not stdout directly.

Fix: redirect stdout to a file when launching the .exe:

  Option A — In your startup script / Task Scheduler:
    MyApp.exe > C:\MyApp\logs\app.log 2>&1

  Option B — In your code (C# example):
    var logFile = new StreamWriter("C:\\MyApp\\logs\\app.log", append: true);
    Console.SetOut(logFile);
    Console.SetError(logFile);

  Option C — Use NLog/Serilog/log4net to write to a file:
    (best option — structured logging with rotation)

  Then add that file path to the SSM Parameter Store config
  (ask your infra team, or update it yourself if you have access).
```

**Step 5: View logs in CloudWatch**

```
┌─────────────────────────────────────────────────────────────────┐
│ Go to: CloudWatch → Log Groups                                  │
│ Search: /nonprod/fusion/ec2                                     │
│                                                                 │
│ START HERE (most likely to have your .exe logs):                │
│   /nonprod/fusion/ec2/windows_events/application                │
│   → Click log stream: i-00acc0413ebfc71f1                       │
│   → Search for your app name or error messages                  │
│                                                                 │
│ ALSO CHECK (if your app is behind IIS):                         │
│   /nonprod/fusion/ec2/w3svc1/default.log                       │
│   → HTTP request logs (client IP, URL, status code, duration)   │
│                                                                 │
│ Use Log Insights to search across all log groups:               │
│                                                                 │
│   -- Find all errors in Application event log                   │
│   fields @timestamp, @message                                   │
│   | filter @message like /Error/                                │
│   | sort @timestamp desc                                        │
│   | limit 50                                                    │
│                                                                 │
│   -- Find IIS 500 errors                                        │
│   fields @timestamp, @message                                   │
│   | filter @message like /500/                                  │
│   | sort @timestamp desc                                        │
│                                                                 │
│ Set up an alarm:                                                │
│   Metric Filter on /nonprod/fusion/ec2/windows_events/application│
│   Pattern: "ERROR" or "Exception"                               │
│   Alarm: if count > 5 in 5 minutes → notify Slack               │
└─────────────────────────────────────────────────────────────────┘
```

**Step 6: View logs in Grafana**

```
Once logs are in CloudWatch, Grafana can read them:

  Grafana → Data Source: CloudWatch
  Query type: CloudWatch Logs
  Log Group: /nonprod/fusion/ec2/windows_events/application
  Query: fields @timestamp, @message | filter @message like /Error/

  OR for IIS logs:
  Log Group: /nonprod/fusion/ec2/w3svc1/default.log
  Query: fields @timestamp, @message | filter @message like /500/

  You get:
  ├── Live log tailing (like watching the console, but in your browser)
  ├── Search across time ranges
  ├── Filter by error level, keywords, patterns
  ├── Correlate with CPU/memory metrics on the same dashboard
  └── Set Grafana alerts on log patterns

  For the METRICS (CPU, RAM, disk, network):
  Grafana → Data Source: CloudWatch
  Namespace: CWAgent
  Metric: Processor → % Processor Time
  Dimension: InstanceId = i-00acc0413ebfc71f1
```

**The complete picture — your EC2 instance's observability (from actual config):**

```
Your EC2 Instance: i-00acc0413ebfc71f1 (10.203.107.242)
Subnet: PRIVATESUBNET-NONPROD-A | Region: ap-southeast-2

                    ┌─────────────────────────────────┐
                    │  EC2 Instance                    │
                    │                                 │
                    │  Your .exe (console app)        │
                    │    │                            │
                    │    ├── Windows Event Log ────────┼──► /nonprod/fusion/ec2/
                    │    │   (Application channel)    │     windows_events/application
                    │    │                            │
                    │    └── Log file (if exists) ────┼──► (not configured yet —
                    │                                 │     needs SSM param update)
                    │                                 │
                    │  IIS Web Server                 │
                    │    └── c:\inetpub\logs\*.log ───┼──► /nonprod/fusion/ec2/
                    │                                 │     w3svc1/default.log
                    │                                 │
                    │  Infrastructure                 │
                    │    ├── healthcheck.log ──────────┼──► /nonprod/fusion/ec2/healthcheck.log
                    │    ├── s3sync.log ──────────────┼──► /nonprod/fusion/ec2/s3sync.log
                    │    ├── cfn-init.log ────────────┼──► /nonprod/fusion/ec2/cfn-init.log
                    │    └── SSM agent logs ──────────┼──► /nonprod/fusion/ec2/ssm.log
                    │                                 │
                    │  Windows Events                 │
                    │    ├── System ──────────────────┼──► .../windows_events/system
                    │    ├── Security ────────────────┼──► .../windows_events/security
                    │    ├── Application ─────────────┼──► .../windows_events/application ✅
                    │    └── CrowdStrike ─────────────┼──► .../windows_events/crowd_strike
                    │                                 │
                    │  Metrics (CWAgent namespace)    │
                    │    ├── CPU % ───────────────────┼──► CloudWatch Metrics
                    │    ├── RAM % ───────────────────┼──► CloudWatch Metrics
                    │    ├── Disk % ──────────────────┼──► CloudWatch Metrics
                    │    ├── Network bytes/sec ───────┼──► CloudWatch Metrics
                    │    └── StatsD :8125 ────────────┼──► CloudWatch Metrics
                    │        (your app can push       │     (custom app metrics)
                    │         custom metrics here)    │
                    └─────────────────────────────────┘
                                    │
                                    ▼
                    ┌─────────────────────────────────┐
                    │  CloudWatch (ap-southeast-2)    │
                    │                                 │
                    │  14 Log Groups                  │
                    │  CWAgent Metrics namespace      │
                    │                                 │
                    │  ├──► Log Insights (search)     │
                    │  ├──► Alarms (alert on errors)  │
                    │  └──► Grafana (dashboards)      │
                    └─────────────────────────────────┘

No more RDP to check logs. Ever.
```

**Bonus: Systems Manager Session Manager (replace RDP entirely)**

```
Even when you DO need a shell on the instance, don't use RDP:

  Go to: Systems Manager → Session Manager → Start session
  Select instance i-00acc0413ebfc71f1
  → You get a PowerShell session in your browser.

  ├── No RDP port (3389) needed in security group
  ├── No key pairs to manage
  ├── All sessions are logged in CloudTrail (audit trail)
  ├── IAM-based access control
  └── Works even though the instance has no public IP (10.203.107.242)

  SSM agent logs are already being shipped to:
  /nonprod/fusion/ec2/ssm.log

  This is the SDE-3 way. RDP is for emergencies only.
```

---

## 16. Metrics & Alarms

### 16.1 The Four Golden Signals

Every service you own must have these monitored:

```
┌─────────────┬──────────────────────────────────────────────────────┐
│ Signal      │ What to Measure                                      │
├─────────────┼──────────────────────────────────────────────────────┤
│ Latency     │ p50, p95, p99 response time                         │
│ Traffic     │ Requests per second, concurrent connections          │
│ Errors      │ 5xx rate, 4xx rate, exception count                  │
│ Saturation  │ CPU%, memory%, disk%, queue depth, connection pool   │
└─────────────┴──────────────────────────────────────────────────────┘
```

### 16.2 CloudWatch Metrics

**Go to:** CloudWatch → Metrics

**Built-in metrics (free):**
```
ECS:
  CPUUtilization, MemoryUtilization (per service)

ALB:
  RequestCount, TargetResponseTime, HTTPCode_Target_5XX_Count
  HealthyHostCount, UnHealthyHostCount
  ActiveConnectionCount, RejectedConnectionCount

RDS:
  CPUUtilization, FreeableMemory, DatabaseConnections
  ReadLatency, WriteLatency, ReadIOPS, WriteIOPS
  FreeStorageSpace, ReplicaLag

ElastiCache:
  CPUUtilization, CurrConnections, CacheHitRate
  Evictions, ReplicationLag

SQS:
  ApproximateNumberOfMessagesVisible (queue depth)
  ApproximateAgeOfOldestMessage
  NumberOfMessagesSent, NumberOfMessagesReceived
```

### 16.3 Custom Metrics

```python
import boto3

cloudwatch = boto3.client('cloudwatch')

# Publish custom metric
cloudwatch.put_metric_data(
    Namespace='MyApp/Production',
    MetricData=[
        {
            'MetricName': 'OrderProcessingTime',
            'Value': 1250,  # milliseconds
            'Unit': 'Milliseconds',
            'Dimensions': [
                {'Name': 'Service', 'Value': 'order-service'},
                {'Name': 'Environment', 'Value': 'production'}
            ]
        },
        {
            'MetricName': 'PaymentFailures',
            'Value': 1,
            'Unit': 'Count',
            'Dimensions': [
                {'Name': 'FailureReason', 'Value': 'card_declined'},
                {'Name': 'Service', 'Value': 'payment-service'}
            ]
        }
    ]
)
```

### 16.4 CloudWatch Alarms

**Go to:** CloudWatch → Alarms

```
Critical Alarms (page on-call):
├── 5xx error rate > 1% for 5 minutes
├── p99 latency > 5 seconds for 5 minutes
├── ECS running task count < desired count for 5 minutes
├── RDS CPU > 90% for 10 minutes
├── RDS free storage < 10GB
├── SQS oldest message age > 30 minutes
├── Healthy host count < 2
└── ECS deployment failure (circuit breaker triggered)

Warning Alarms (notify Slack):
├── 5xx error rate > 0.1% for 10 minutes
├── p95 latency > 2 seconds for 10 minutes
├── CPU utilization > 70% for 15 minutes
├── Memory utilization > 80% for 15 minutes
├── Cache hit rate < 80%
├── SQS queue depth > 10,000
└── RDS connection count > 80% of max
```

**Alarm actions:**
- SNS → PagerDuty/OpsGenie (for paging)
- SNS → Slack webhook Lambda (for notifications)
- Auto Scaling action (scale up/down)
- EC2 action (reboot, stop, terminate)

---

## 17. Distributed Tracing

### 17.1 Why Tracing Matters

In a microservices architecture, a single user request might touch 5-10 services. When something is slow, you need to know WHERE.

```
User Request (trace_id: abc123)
│
├── API Gateway (50ms)
│   └── Auth Service (20ms)
│       └── Redis lookup (2ms)
│
├── Order Service (200ms)  ← why is this slow?
│   ├── Inventory Service (30ms)
│   │   └── DynamoDB query (5ms)
│   ├── Payment Service (150ms)  ← found it!
│   │   └── External Payment API (140ms)  ← external dependency
│   └── Database query (15ms)
│
└── Notification Service (async, via SQS)
    └── Email Service (500ms)
```

### 17.2 AWS X-Ray

**Go to:** CloudWatch → X-Ray traces → Service map

X-Ray gives you:
- **Service map**: visual graph of how services connect.
- **Traces**: end-to-end request flow with timing.
- **Segments/subsegments**: breakdown within each service.
- **Annotations**: searchable metadata on traces.
- **Groups**: filter traces by criteria (e.g., "error traces only").

### 17.3 OpenTelemetry (OTel) — The Industry Standard

OpenTelemetry is vendor-neutral. Instrument once, send to X-Ray, Grafana Tempo, Jaeger, Datadog, etc.

```python
# Python OpenTelemetry setup
from opentelemetry import trace
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter

# Configure
provider = TracerProvider()
processor = BatchSpanProcessor(OTLPSpanExporter(endpoint="http://otel-collector:4317"))
provider.add_span_processor(processor)
trace.set_tracer_provider(provider)

tracer = trace.get_tracer(__name__)

# Use in code
@tracer.start_as_current_span("process_order")
def process_order(order_id):
    span = trace.get_current_span()
    span.set_attribute("order.id", order_id)

    with tracer.start_as_current_span("validate_payment"):
        validate_payment(order_id)

    with tracer.start_as_current_span("update_inventory"):
        update_inventory(order_id)
```

**ADOT (AWS Distro for OpenTelemetry):** AWS's distribution of OTel. Run as a sidecar container in ECS to collect and export traces/metrics.

---

## 18. Grafana Deep Dive

### 18.1 Grafana Architecture in AWS

```
Data Sources                    Grafana                         Users
├── CloudWatch  ──────────►   ┌──────────────┐   ◄──────────  Developers
├── Prometheus  ──────────►   │  Dashboards   │   ◄──────────  SREs
├── Loki (logs) ──────────►   │  Alerts       │   ◄──────────  On-call
├── Tempo (traces) ───────►   │  Explore      │   ◄──────────  Managers
├── Elasticsearch ────────►   │  Annotations  │
├── X-Ray       ──────────►   └──────────────┘
└── JSON API    ──────────►
```

**Go to:** Your company's Grafana instance (usually `grafana.internal.yourcompany.com`)

### 18.2 Amazon Managed Grafana (AMG)

If your company uses AMG:
- **Go to:** AWS Console → Amazon Managed Grafana
- Fully managed, auto-scaling, SSO integration.
- Native integration with CloudWatch, X-Ray, Prometheus, Timestream.
- IAM-based data source authentication (no API keys to manage).

### 18.3 Data Sources — Setting Up

**Go to:** Grafana → Configuration → Data Sources

```
Essential Data Sources:
├── CloudWatch
│   ├── Auth: IAM role (workspace role for AMG, or access key for self-hosted)
│   ├── Default region: us-east-1
│   └── Namespaces: AWS/ECS, AWS/ApplicationELB, AWS/RDS, AWS/SQS, Custom
│
├── Prometheus (if using Amazon Managed Prometheus or self-hosted)
│   ├── URL: https://aps-workspaces.us-east-1.amazonaws.com/workspaces/ws-xxxxx
│   ├── Auth: SigV4
│   └── Used for: container metrics, custom app metrics via OTel
│
├── Loki (log aggregation — Grafana's log backend)
│   ├── URL: http://loki:3100
│   └── Used for: searching logs alongside metrics (correlate)
│
├── Tempo (distributed tracing — Grafana's trace backend)
│   ├── URL: http://tempo:3200
│   └── Used for: viewing traces linked from logs/metrics
│
└── Elasticsearch / OpenSearch
    ├── URL: https://search-my-domain.us-east-1.es.amazonaws.com
    └── Used for: application logs if using ELK stack
```

### 18.4 Building Dashboards — SDE-3 Level

**Go to:** Grafana → Dashboards → New Dashboard

**Service Overview Dashboard (every service needs one):**

```
Row 1: Traffic & Availability
├── Panel: Request Rate (req/s) — timeseries
│   Query: CloudWatch → AWS/ApplicationELB → RequestCount (Sum, 1m)
├── Panel: Error Rate (%) — timeseries + threshold
│   Query: (5xx count / total count) * 100
├── Panel: Availability (%) — stat panel
│   Query: 100 - error_rate, thresholds: green >99.9, yellow >99, red <99
└── Panel: Active Connections — timeseries
    Query: CloudWatch → ActiveConnectionCount

Row 2: Latency
├── Panel: Response Time (p50, p95, p99) — timeseries with 3 queries
│   Query: CloudWatch → TargetResponseTime → p50, p95, p99
├── Panel: Latency Heatmap — heatmap panel
│   Query: Prometheus → histogram_quantile
└── Panel: Slow Requests (>5s) — stat panel
    Query: count of requests where duration > 5000ms

Row 3: Resources
├── Panel: CPU Utilization — timeseries
│   Query: CloudWatch → AWS/ECS → CPUUtilization
├── Panel: Memory Utilization — timeseries
│   Query: CloudWatch → AWS/ECS → MemoryUtilization
├── Panel: Running Tasks — stat panel
│   Query: CloudWatch → AWS/ECS → RunningTaskCount
└── Panel: Healthy Hosts — stat panel
    Query: CloudWatch → HealthyHostCount

Row 4: Dependencies
├── Panel: Database Connections — timeseries
│   Query: CloudWatch → AWS/RDS → DatabaseConnections
├── Panel: DB Latency (read/write) — timeseries
│   Query: CloudWatch → ReadLatency, WriteLatency
├── Panel: Cache Hit Rate — gauge
│   Query: CloudWatch → AWS/ElastiCache → CacheHitRate
└── Panel: Queue Depth — timeseries
    Query: CloudWatch → AWS/SQS → ApproximateNumberOfMessagesVisible

Row 5: Deployments & Events
└── Panel: Annotations overlay showing deployments, incidents, scaling events
```

### 18.5 Grafana Query Examples (CloudWatch)

```
# ECS CPU by service
Namespace: AWS/ECS
Metric: CPUUtilization
Dimensions: ClusterName=production, ServiceName=web-service
Statistic: Average
Period: 60s

# ALB 5xx errors
Namespace: AWS/ApplicationELB
Metric: HTTPCode_Target_5XX_Count
Dimensions: LoadBalancer=app/my-alb/xxxxx
Statistic: Sum
Period: 60s

# Custom metric
Namespace: MyApp/Production
Metric: OrderProcessingTime
Dimensions: Service=order-service
Statistic: p99
Period: 60s
```

### 18.6 Grafana Query Examples (Prometheus / PromQL)

```promql
# Request rate per second
rate(http_requests_total{service="web", environment="production"}[5m])

# Error rate percentage
sum(rate(http_requests_total{status=~"5.."}[5m]))
/
sum(rate(http_requests_total[5m])) * 100

# P99 latency
histogram_quantile(0.99, sum(rate(http_request_duration_seconds_bucket[5m])) by (le))

# CPU usage per container
sum(rate(container_cpu_usage_seconds_total{namespace="production"}[5m])) by (pod)

# Memory usage percentage
container_memory_working_set_bytes{namespace="production"}
/
container_spec_memory_limit_bytes{namespace="production"} * 100
```

### 18.7 Grafana Loki (Log Queries)

```logql
# All error logs from web service
{service="web-service", environment="production"} |= "ERROR"

# Parse JSON logs and filter
{service="web-service"} | json | level="error" | duration_ms > 5000

# Count errors per minute by type
sum(rate({service="web-service"} |= "ERROR" | json | unwrap error_count [1m])) by (error_type)

# Correlate: find logs for a specific trace
{service="web-service"} |= "trace_id=abc123def456"
```

### 18.8 Grafana Alerts

**Go to:** Grafana → Alerting → Alert Rules

```yaml
Alert: High Error Rate
  Query: 5xx_count / total_count * 100
  Condition: WHEN last() OF query IS ABOVE 1
  For: 5 minutes
  Labels:
    severity: critical
    team: backend
  Annotations:
    summary: "Error rate is {{ $value }}% on {{ $labels.service }}"
    runbook: "https://wiki.internal/runbooks/high-error-rate"
  Notification:
    - PagerDuty (critical)
    - Slack #alerts-backend (all)

Alert: High Latency
  Query: p99 response time
  Condition: WHEN last() OF query IS ABOVE 5000 (ms)
  For: 5 minutes
  Labels:
    severity: warning
  Notification:
    - Slack #alerts-backend

Alert: Low Cache Hit Rate
  Query: ElastiCache CacheHitRate
  Condition: WHEN last() IS BELOW 70
  For: 15 minutes
  Labels:
    severity: warning
  Notification:
    - Slack #alerts-backend
```

### 18.9 Grafana — Correlating Metrics, Logs, and Traces

This is the killer feature. In a single Grafana instance:

```
1. You see a spike in error rate on your dashboard (METRIC)
2. Click on the spike → "Explore" → switch to Loki data source
3. See the actual error logs at that timestamp (LOG)
4. Click on a trace_id in the log line → switch to Tempo data source
5. See the full distributed trace showing which service/call failed (TRACE)

Metrics → Logs → Traces — all connected by timestamp and trace_id
```

**To enable this correlation:**
- Your logs must include `trace_id` field.
- Configure "derived fields" in Loki data source to link trace_id → Tempo.
- Configure "exemplars" in Prometheus to link metrics → traces.

---

## 19. Incident Response & On-Call

### 19.1 Incident Severity Levels

```
┌──────┬──────────────────────────────────────────────────────────────┐
│ Sev  │ Definition                                                   │
├──────┼──────────────────────────────────────────────────────────────┤
│ SEV1 │ Complete service outage. All users affected. Revenue impact. │
│      │ Response: Immediately. War room. All hands.                  │
├──────┼──────────────────────────────────────────────────────────────┤
│ SEV2 │ Major degradation. Many users affected. Partial outage.      │
│      │ Response: Within 15 minutes. Primary on-call + backup.       │
├──────┼──────────────────────────────────────────────────────────────┤
│ SEV3 │ Minor degradation. Some users affected. Workaround exists.   │
│      │ Response: Within 1 hour. Primary on-call.                    │
├──────┼──────────────────────────────────────────────────────────────┤
│ SEV4 │ Cosmetic issue. Minimal impact. No user-facing degradation.  │
│      │ Response: Next business day.                                 │
└──────┴──────────────────────────────────────────────────────────────┘
```

### 19.2 Incident Response Playbook

```
1. DETECT
   ├── Alert fires (PagerDuty/OpsGenie)
   ├── Customer report
   └── Monitoring dashboard anomaly

2. TRIAGE (first 5 minutes)
   ├── Acknowledge the alert
   ├── Check: Is this real? (not a false alarm)
   ├── Determine severity level
   ├── Check: Was there a recent deployment? → ROLLBACK FIRST
   └── Communicate: post in #incidents Slack channel

3. INVESTIGATE (5-30 minutes)
   ├── Open Grafana service dashboard
   ├── Check error rate, latency, traffic patterns
   ├── Check CloudWatch Logs / Log Insights for errors
   ├── Check ECS service events (failed deployments, OOM kills)
   ├── Check dependency health (DB, cache, queues, external APIs)
   ├── Check AWS Health Dashboard for regional issues
   └── Check recent changes (deployments, config changes, infra changes)

4. MITIGATE (minimize impact)
   ├── Rollback deployment (if recent deploy)
   ├── Scale up (if capacity issue)
   ├── Failover to standby (if AZ/region issue)
   ├── Enable circuit breaker (if dependency issue)
   ├── Redirect traffic (if specific endpoint issue)
   └── Communicate: update #incidents with status

5. RESOLVE
   ├── Confirm metrics are back to normal
   ├── Confirm no new errors in logs
   ├── Run smoke tests
   └── Communicate: incident resolved

6. POST-MORTEM (within 48 hours)
   ├── Timeline of events
   ├── Root cause analysis (5 Whys)
   ├── What went well
   ├── What went poorly
   ├── Action items with owners and deadlines
   └── Share with broader team
```

### 19.3 Common ECS Troubleshooting

```
Problem: Tasks keep restarting
  Check: ECS → Service → Events tab
  Check: CloudWatch Logs for OOM kills or crash logs
  Check: Task definition health check (is startPeriod long enough?)
  Check: Container exit code (137 = OOM killed, 1 = app error)

Problem: Deployment stuck
  Check: ECS → Service → Deployments tab
  Check: New tasks failing health checks?
  Check: Target group health in ALB
  Fix: Circuit breaker should auto-rollback. If not, manually update service to previous task def.

Problem: Tasks can't pull image from ECR
  Check: Task execution role has ecr:GetAuthorizationToken, ecr:BatchGetImage
  Check: VPC endpoint for ECR exists (if private subnet)
  Check: NAT Gateway is working (if no VPC endpoint)

Problem: Tasks can't connect to RDS
  Check: Security group allows traffic from ECS task SG to RDS SG on port 5432/3306
  Check: Tasks are in the correct subnets
  Check: RDS is in the same VPC (or VPC peering is configured)
  Check: Connection string and credentials are correct (Secrets Manager)

Problem: High memory usage / OOM
  Check: Container memory limit in task definition
  Check: Application memory leaks (heap dumps, profiling)
  Fix: Increase task memory, fix the leak, or add more tasks
```

---

## 20. Cost Optimization & FinOps

### 20.1 Where the Money Goes

**Go to:** AWS Console → Billing → Cost Explorer

```
Typical AWS Cost Breakdown:
├── EC2 / ECS Fargate (compute): 40-50%
├── RDS / Aurora (database): 15-25%
├── Data Transfer: 10-15%
├── NAT Gateway: 5-10% (often a surprise)
├── S3: 3-5%
├── ElastiCache: 3-5%
├── CloudWatch (logs): 2-5%
├── Load Balancers: 2-3%
└── Everything else: 5-10%
```

### 20.2 Cost Optimization Strategies

```
Compute:
├── Right-size instances (use AWS Compute Optimizer recommendations)
├── Use Spot instances for fault-tolerant workloads (60-90% savings)
├── Reserved Instances or Savings Plans for steady-state (30-60% savings)
├── Fargate Spot for non-critical ECS tasks
├── Scale down dev/staging environments at night and weekends
└── Use ARM-based instances (Graviton) — 20% cheaper, often faster

Database:
├── Use Aurora Serverless v2 for variable workloads
├── Reserved instances for production databases
├── Stop dev/staging databases when not in use
├── Right-size: don't use db.r5.4xlarge when db.r5.large works
└── Use read replicas instead of scaling up the primary

Storage:
├── S3 lifecycle policies (move to IA/Glacier)
├── Delete unused EBS snapshots
├── Delete unattached EBS volumes
├── ECR lifecycle policies (delete old images)
└── CloudWatch log retention policies

Networking:
├── VPC endpoints for S3/DynamoDB (avoid NAT Gateway charges)
├── Use VPC endpoints for ECR, CloudWatch, etc. if high traffic
├── NAT Gateway: $0.045/GB processed — this adds up fast
├── Data transfer: keep traffic within AZ when possible
└── Use CloudFront to reduce origin data transfer

Monitoring:
├── CloudWatch Logs: reduce log verbosity in production
├── CloudWatch Metrics: use 1-minute period only for critical metrics
├── X-Ray: sample traces (don't trace 100% of requests)
└── Delete unused dashboards and alarms
```

### 20.3 Savings Plans & Reserved Instances

```
Commitment Options:
├── Compute Savings Plans: flexible across instance types/regions (best for most)
│   └── 1-year no upfront: ~30% savings
│   └── 3-year all upfront: ~60% savings
├── EC2 Instance Savings Plans: locked to instance family in a region
│   └── Slightly cheaper than Compute SP
├── Reserved Instances: locked to specific instance type
│   └── Cheapest but least flexible
└── Fargate Savings Plans: commit to $/hour of Fargate usage
```

**SDE-3 tip:** Set up AWS Budgets with alerts. Know your team's monthly spend. Flag anomalies early.

---

## 21. Security & Compliance

### 21.1 Security Layers

```
┌─────────────────────────────────────────────────────────────┐
│ Layer 1: Edge Security                                       │
│ ├── CloudFront + WAF (rate limiting, geo-blocking, OWASP)   │
│ ├── Shield (DDoS protection — Standard is free)              │
│ └── Route 53 (DNS-level health checks and failover)          │
├─────────────────────────────────────────────────────────────┤
│ Layer 2: Network Security                                    │
│ ├── VPC isolation (public/private subnets)                   │
│ ├── Security Groups (instance-level firewall)                │
│ ├── NACLs (subnet-level firewall)                            │
│ └── VPC Flow Logs (network traffic audit)                    │
├─────────────────────────────────────────────────────────────┤
│ Layer 3: Identity & Access                                   │
│ ├── IAM roles with least privilege                           │
│ ├── SSO / SAML federation (no long-lived credentials)        │
│ ├── MFA everywhere                                           │
│ └── Service-to-service auth (IAM roles, not API keys)        │
├─────────────────────────────────────────────────────────────┤
│ Layer 4: Data Security                                       │
│ ├── Encryption at rest (KMS for EBS, S3, RDS, etc.)          │
│ ├── Encryption in transit (TLS everywhere)                   │
│ ├── Secrets Manager (no hardcoded secrets)                   │
│ └── S3 Block Public Access (account-level)                   │
├─────────────────────────────────────────────────────────────┤
│ Layer 5: Application Security                                │
│ ├── Input validation and sanitization                        │
│ ├── Dependency scanning (Snyk, Dependabot, ECR scanning)     │
│ ├── SAST/DAST in CI pipeline                                 │
│ └── Container image scanning                                 │
├─────────────────────────────────────────────────────────────┤
│ Layer 6: Audit & Detection                                   │
│ ├── CloudTrail (API audit log — who did what, when)          │
│ ├── GuardDuty (threat detection — anomalous API calls)       │
│ ├── Config (resource compliance — is encryption enabled?)    │
│ ├── Security Hub (aggregated security findings)              │
│ └── Inspector (vulnerability scanning for EC2/ECR/Lambda)    │
└─────────────────────────────────────────────────────────────┘
```

### 21.2 AWS Security Services to Know

**Go to:** Each of these in the AWS Console

```
CloudTrail:
  What: Logs every AWS API call (who created that S3 bucket? who changed that SG?)
  Action: Ensure it's enabled, sending to S3, with log file validation.

GuardDuty:
  What: ML-based threat detection. Finds compromised instances, unusual API calls,
        cryptocurrency mining, data exfiltration.
  Action: Enable it. Review findings weekly.

Security Hub:
  What: Aggregates findings from GuardDuty, Inspector, Config, IAM Access Analyzer.
        Runs CIS Benchmark and AWS Foundational Security checks.
  Action: Enable it. Fix critical/high findings.

AWS Config:
  What: Tracks resource configuration changes over time. Runs compliance rules.
  Action: Enable rules like "s3-bucket-public-read-prohibited",
          "rds-instance-public-access-check", "encrypted-volumes".

Inspector:
  What: Scans EC2 instances and ECR images for software vulnerabilities.
  Action: Enable for all ECR repos. Fix critical CVEs before deploying.
```

### 21.3 Security Checklist for SDE-3

```
□ No AWS access keys in code (use IAM roles, OIDC)
□ All S3 buckets have Block Public Access enabled
□ All data encrypted at rest (EBS, S3, RDS, ElastiCache)
□ All data encrypted in transit (TLS 1.2+)
□ Security groups follow least privilege (no 0.0.0.0/0 except ALB 443)
□ IAM policies follow least privilege
□ Secrets in Secrets Manager, not env vars or code
□ ECR image scanning enabled
□ Dependency scanning in CI pipeline
□ CloudTrail enabled and monitored
□ GuardDuty enabled
□ VPC Flow Logs enabled for production
□ No SSH key pairs — use Session Manager
□ MFA on all human IAM users (better: use SSO)
□ Regular access reviews (who has access to what?)
```

---

## 22. Performance Engineering

### 22.1 Performance Testing

```
Types of Performance Tests:
├── Load Test: expected traffic volume, sustained
├── Stress Test: beyond expected, find breaking point
├── Spike Test: sudden traffic surge
├── Soak Test: sustained load over hours (find memory leaks)
└── Capacity Test: determine max throughput

Tools:
├── k6 (modern, scriptable, developer-friendly)
├── Locust (Python-based)
├── JMeter (Java, GUI-based, enterprise)
├── Artillery (Node.js, YAML config)
└── AWS Distributed Load Testing (CloudFormation solution)
```

**k6 example:**
```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '2m', target: 100 },   // ramp up to 100 users
    { duration: '5m', target: 100 },   // stay at 100
    { duration: '2m', target: 500 },   // ramp up to 500
    { duration: '5m', target: 500 },   // stay at 500
    { duration: '2m', target: 0 },     // ramp down
  ],
  thresholds: {
    http_req_duration: ['p(99)<1500'],  // 99% of requests under 1.5s
    http_req_failed: ['rate<0.01'],     // less than 1% errors
  },
};

export default function () {
  const res = http.get('https://api.yourcompany.com/health');
  check(res, {
    'status is 200': (r) => r.status === 200,
    'response time < 500ms': (r) => r.timings.duration < 500,
  });
  sleep(1);
}
```

### 22.2 Application Performance Optimization

```
Common Bottlenecks & Fixes:
├── Database queries
│   ├── Add indexes (check EXPLAIN ANALYZE)
│   ├── Use connection pooling (RDS Proxy / PgBouncer)
│   ├── Cache frequent queries (Redis)
│   ├── Use read replicas for read-heavy workloads
│   └── Optimize N+1 queries (batch/join)
│
├── External API calls
│   ├── Add timeouts (always!)
│   ├── Add circuit breakers (fail fast)
│   ├── Cache responses where possible
│   ├── Use async/parallel calls
│   └── Add retry with exponential backoff
│
├── Memory
│   ├── Profile heap usage
│   ├── Fix memory leaks (common in long-running processes)
│   ├── Right-size container memory limits
│   └── Use streaming for large payloads (don't load entire file in memory)
│
├── CPU
│   ├── Profile hot paths
│   ├── Offload heavy computation to worker queues
│   ├── Use compiled languages for CPU-bound tasks
│   └── Right-size container CPU limits
│
└── Network
    ├── Enable compression (gzip/brotli)
    ├── Use connection keep-alive
    ├── Minimize payload sizes
    ├── Use CDN for static assets
    └── Keep services in the same AZ when possible
```

### 22.3 Timeouts & Circuit Breakers

```python
# Every external call MUST have a timeout
import requests
from circuitbreaker import circuit

@circuit(failure_threshold=5, recovery_timeout=30)
def call_payment_service(order_id):
    response = requests.post(
        "https://payment-service.internal/charge",
        json={"order_id": order_id},
        timeout=(3, 10)  # (connect_timeout, read_timeout) in seconds
    )
    response.raise_for_status()
    return response.json()

# Retry with exponential backoff
from tenacity import retry, stop_after_attempt, wait_exponential

@retry(
    stop=stop_after_attempt(3),
    wait=wait_exponential(multiplier=1, min=1, max=10)
)
def call_with_retry():
    return call_payment_service(order_id)
```

**SDE-3 rule:** If you call any external service (database, cache, API, queue) without a timeout, you have a production incident waiting to happen.

---

## 23. Disaster Recovery & High Availability

### 23.1 HA Architecture

```
Region: us-east-1
├── AZ: us-east-1a
│   ├── Public Subnet: ALB node, NAT Gateway
│   ├── Private Subnet: ECS tasks (2), RDS primary
│   └── Private Subnet: ElastiCache primary
│
├── AZ: us-east-1b
│   ├── Public Subnet: ALB node, NAT Gateway
│   ├── Private Subnet: ECS tasks (2), RDS standby (Multi-AZ)
│   └── Private Subnet: ElastiCache replica
│
└── AZ: us-east-1c (optional, for extra redundancy)
    ├── Public Subnet: ALB node
    └── Private Subnet: ECS tasks (1), RDS read replica
```

### 23.2 DR Strategies

```
┌──────────────────┬──────────┬──────────┬─────────────────────────────┐
│ Strategy         │ RTO      │ RPO      │ Cost                        │
├──────────────────┼──────────┼──────────┼─────────────────────────────┤
│ Backup & Restore │ Hours    │ Hours    │ $ (cheapest)                │
│ Pilot Light      │ Minutes  │ Minutes  │ $$ (minimal infra running)  │
│ Warm Standby     │ Minutes  │ Seconds  │ $$$ (scaled-down copy)      │
│ Multi-Region     │ Seconds  │ Near-zero│ $$$$ (full copy in 2nd region)│
│ Active-Active    │ Zero     │ Zero     │ $$$$$ (both regions serving) │
└──────────────────┴──────────┴──────────┴─────────────────────────────┘

RTO = Recovery Time Objective (how long to recover)
RPO = Recovery Point Objective (how much data can you lose)
```

### 23.3 Multi-Region Setup

```
Route 53 (DNS — latency-based or failover routing)
├── us-east-1 (primary)
│   ├── ALB → ECS Service
│   ├── Aurora Global Database (writer)
│   ├── ElastiCache (primary)
│   ├── S3 (cross-region replication to us-west-2)
│   └── SQS queues
│
└── us-west-2 (secondary)
    ├── ALB → ECS Service (scaled down or full)
    ├── Aurora Global Database (reader, promotable)
    ├── ElastiCache (independent cluster)
    ├── S3 (replica bucket)
    └── SQS queues
```

### 23.4 Backup Strategy

```
Automated Backups:
├── RDS: automated backups, 7-day retention (production: 35 days)
├── DynamoDB: point-in-time recovery (PITR) enabled, 35-day window
├── EBS: automated snapshots via AWS Backup
├── S3: versioning enabled (objects recoverable)
├── Aurora: continuous backup to S3, 35-day retention
├── ElastiCache: daily snapshots, 7-day retention
└── Secrets Manager: versioned (previous versions recoverable)

Test your backups:
├── Monthly: restore RDS from snapshot to verify data integrity
├── Quarterly: full DR drill (failover to secondary region)
└── Annually: chaos engineering exercise (simulate AZ failure)
```

---

## 24. Real-World Scenarios & Runbooks

### 24.1 Scenario: Deploy a New Microservice End-to-End

```
Step 1: Code & Containerize
  ├── Write application code
  ├── Create Dockerfile (multi-stage build)
  ├── Create health check endpoint (/health)
  └── Add structured logging and tracing

Step 2: Infrastructure (Terraform/CloudFormation)
  ├── ECR repository
  ├── ECS task definition
  ├── ECS service
  ├── ALB target group + listener rule
  ├── Security groups
  ├── IAM roles (task role + execution role)
  ├── CloudWatch log group
  ├── Secrets Manager entries
  └── DNS record (Route 53)

Step 3: CI/CD Pipeline
  ├── Build stage: docker build, push to ECR
  ├── Test stage: unit tests, integration tests
  ├── Deploy to staging: update ECS service
  ├── Smoke tests on staging
  ├── Deploy to production: update ECS service
  └── Post-deploy verification

Step 4: Observability
  ├── Grafana dashboard (4 golden signals)
  ├── CloudWatch alarms (error rate, latency, health)
  ├── PagerDuty/OpsGenie integration
  ├── Log queries saved in CloudWatch Insights
  └── Runbook documented

Step 5: Security
  ├── ECR image scanning
  ├── IAM least privilege review
  ├── Security group review
  ├── Secrets rotation configured
  └── Dependency scanning in CI
```

### 24.2 Scenario: Investigate a Latency Spike

```
1. Open Grafana → Service Dashboard
   └── Identify: when did latency spike? which percentile? (p50 vs p99)

2. Check: was there a deployment at that time?
   └── Yes → likely the deploy. Check new code for regressions. Consider rollback.
   └── No → continue investigating.

3. Check dependencies:
   ├── Database: RDS CPU, connection count, slow query log
   ├── Cache: hit rate drop? evictions spike?
   ├── Queue: depth increasing? consumer lag?
   └── External APIs: timeout rate increase?

4. Check resources:
   ├── ECS CPU/memory: are tasks maxed out?
   ├── Task count: did scaling fail?
   └── ALB: connection count, spillover count

5. Check logs:
   └── CloudWatch Log Insights:
       fields @timestamp, endpoint, duration_ms
       | filter duration_ms > 5000
       | sort duration_ms desc
       | limit 50

6. Check traces:
   └── X-Ray / Tempo: find slow traces, identify which span is slow

7. Mitigate:
   ├── Scale up (more tasks)
   ├── Cache the slow query
   ├── Add timeout to slow dependency
   ├── Rollback if deploy-related
   └── Contact dependency team if external
```

### 24.3 Scenario: Handle a Traffic Spike (10x Normal)

```
Immediate:
├── ECS auto-scaling should handle it (if configured correctly)
├── Check: are new tasks launching? (ECS events)
├── Check: ALB is distributing traffic? (no spillover)
├── Check: database can handle the load? (connection pooling, read replicas)
└── Check: cache is absorbing reads? (hit rate should be high)

If auto-scaling isn't enough:
├── Manually increase ECS desired count
├── Increase RDS instance size (requires brief downtime or use Aurora auto-scaling)
├── Add ElastiCache read replicas
├── Enable CloudFront caching for API responses (if applicable)
├── Implement request throttling / rate limiting at ALB (WAF)
└── Queue non-critical work (move to async processing via SQS)

Post-spike:
├── Review: did auto-scaling work as expected?
├── Adjust scaling policies if needed
├── Consider pre-warming for predictable spikes
└── Document lessons learned
```

### 24.4 Scenario: Database Migration (Zero Downtime)

```
Phase 1: Prepare (backward-compatible schema change)
  ├── Add new column (nullable, with default)
  ├── Deploy code that writes to BOTH old and new columns
  └── Verify: old code still works, new column is being populated

Phase 2: Backfill
  ├── Run migration script to populate new column for existing rows
  ├── Do this in batches (not one giant UPDATE)
  └── Verify: all rows have new column populated

Phase 3: Switch
  ├── Deploy code that reads from new column
  ├── Code still writes to both columns (safety net)
  └── Verify: application works correctly with new column

Phase 4: Cleanup
  ├── Deploy code that only uses new column
  ├── Drop old column (or rename, keep for safety period)
  └── Verify: clean state

NEVER in a single deploy:
  ✗ Drop a column
  ✗ Rename a column
  ✗ Change a column type
  ✗ Add a NOT NULL constraint without default
```

---

## 25. Learning Path & Next Steps

### 25.1 Hands-On Exercises (Do These in Your Company's AWS Account)

```
Week 1-2: Networking & Compute
  □ Map out your VPC topology (draw it)
  □ Trace a request from internet → ALB → ECS task → database
  □ Find all security groups for your service, document the rules
  □ SSH into an EC2 instance using Session Manager (not SSH keys)
  □ Review your ASG scaling policies

Week 3-4: Containers & Deployment
  □ Read your ECS task definition JSON, understand every field
  □ Watch an ECS deployment happen in real-time (Events tab)
  □ Use ECS Exec to shell into a running Fargate task
  □ Trigger a rollback by deploying a broken image
  □ Review your CI/CD pipeline end-to-end

Week 5-6: Observability
  □ Build a Grafana dashboard for your service from scratch
  □ Write 5 CloudWatch Log Insights queries you'd use during incidents
  □ Set up a CloudWatch alarm that pages you
  □ Trace a request end-to-end using X-Ray or Tempo
  □ Correlate a metric spike → logs → trace in Grafana

Week 7-8: Data & Caching
  □ Review your RDS configuration against the production checklist
  □ Enable Performance Insights, find your slowest queries
  □ Review your S3 buckets, add lifecycle policies where missing
  □ Check your ElastiCache metrics, calculate cache hit rate
  □ Review your SQS queues, check DLQ for failed messages

Week 9-10: Security & Cost
  □ Run IAM Access Analyzer, review findings
  □ Enable and review GuardDuty findings
  □ Review Cost Explorer, identify top 5 cost drivers
  □ Find at least 3 cost optimization opportunities
  □ Review CloudTrail for unusual API activity

Week 11-12: Advanced
  □ Write a Terraform module for a new service
  □ Conduct a load test with k6
  □ Participate in (or run) a DR drill
  □ Write a runbook for your service's most common failure mode
  □ Present your findings to your team
```

### 25.2 AWS Certifications (Recommended for SDE-3)

```
├── AWS Solutions Architect Associate (foundational, broad coverage)
├── AWS Solutions Architect Professional (deep, scenario-based)
├── AWS DevOps Engineer Professional (CI/CD, monitoring, automation)
└── AWS Security Specialty (if security-focused role)
```

### 25.3 Books & Resources

```
Books:
├── "Designing Data-Intensive Applications" — Martin Kleppmann
├── "Site Reliability Engineering" — Google SRE Book (free online)
├── "The Phoenix Project" — Gene Kim (DevOps culture)
├── "Accelerate" — Nicole Forsgren (engineering metrics)
└── "Database Internals" — Alex Petrov

Online:
├── AWS Well-Architected Framework (read all 6 pillars)
├── AWS Architecture Blog
├── Last Week in AWS (newsletter — great for staying current)
├── Grafana Labs blog and documentation
└── OpenTelemetry documentation
```

---

## Quick Reference Card

```
┌─────────────────────────────────────────────────────────────────────┐
│                    SDE-3 DAILY CHECKLIST                            │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Morning:                                                           │
│  □ Check Grafana dashboards — any anomalies overnight?              │
│  □ Check PagerDuty/OpsGenie — any alerts fired?                     │
│  □ Check deployment pipeline — any failed builds?                   │
│  □ Check SQS DLQs — any failed messages?                            │
│                                                                     │
│  Before deploying:                                                  │
│  □ Tests pass? Image scanned? Migrations safe?                      │
│  □ Feature flag in place? Rollback plan ready?                      │
│  □ Dashboard open? On-call aware?                                   │
│                                                                     │
│  After deploying:                                                   │
│  □ Error rate stable? Latency stable?                               │
│  □ No new exceptions in logs?                                       │
│  □ Health checks passing?                                           │
│                                                                     │
│  Weekly:                                                            │
│  □ Review cost trends                                               │
│  □ Review security findings                                         │
│  □ Review slow queries / performance metrics                        │
│  □ Review and clean up unused resources                             │
│  □ Update runbooks if anything changed                              │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

> **Remember:** The best way to learn infrastructure is to break it (in staging) and fix it.
> Open your AWS Console, open Grafana, and start clicking around. Every service mentioned
> in this guide exists in your company's account — go find it, understand it, own it.
