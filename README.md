# AI-Assisted Adaptive English Learning System

## Overview

AI-Assisted Adaptive English Learning System is a software engineering graduation project designed to support personalized English learning. The system helps learners take placement tests, follow adaptive learning paths, complete lessons and quizzes, track progress, receive recommendations, and improve weak skills through AI-assisted analysis.

## Main Features

### Learner Features

* Register and login
* Take placement test
* View current English level
* Follow personalized learning path
* Study lessons by skill and level
* Take quizzes and receive instant results
* Track learning progress
* View skill matrix
* Receive recommended lessons
* Set learning goals
* Receive achievements and badges
* Get learning reminders and notifications
* Send feedback about lessons, quizzes, and recommendations

### Admin Features

* Manage users and learners
* Manage lessons
* Manage quizzes and questions
* View learner progress
* View feedback
* Manage achievements
* View dashboard and reports

## System Architecture

The system follows a microservices and event-driven architecture.

Main components:

* **Frontend Learner Web App**
* **Frontend Admin Web App**
* **Core Learning REST API**
* **Adaptive / Event / AI Backend**
* **gRPC Recommendation Service**
* **Kafka Message Broker**
* **Redis Cache**
* **Background Worker**
* **Database**
* **Docker Deployment**

## Technology Stack

| Component             | Technology                |
| --------------------- | ------------------------- |
| Backend API           | .NET 8 / ASP.NET Core     |
| Database              | SQL Server or PostgreSQL  |
| Message Broker        | Apache Kafka              |
| Cache                 | Redis                     |
| Service Communication | gRPC                      |
| Background Job        | Worker Service / Hangfire |
| Frontend              | React                     |
| Authentication        | JWT                       |
| Deployment            | Docker / Docker Compose   |

## Team Branches

| Member | Role                                 | Branch                          |
| ------ | ------------------------------------ | ------------------------------- |
| Hoàng  | Backend 1 - Core Learning System     | `feature/hoang-backend-core`    |
| Huy    | Backend 2 - Adaptive/Event/AI System | `feature/huy-backend-adaptive`  |
| Tùng   | Frontend 1 - Learner Web App         | `feature/tung-frontend-learner` |
| Khoa   | Frontend 2 - Admin Web App           | `feature/khoa-frontend-admin`   |

## Branch Strategy

* `main`: Stable version for demo/submission
* `develop`: Main development integration branch
* `feature/*`: Individual feature branches for each member

Workflow:

```bash
feature branch -> pull request -> develop -> main
```

## Core Learning Flow

```text
Learner registers/logs in
        ↓
Takes placement test
        ↓
System creates skill matrix
        ↓
System generates learning path
        ↓
Learner studies lessons and takes quizzes
        ↓
REST API saves result and publishes event to Kafka
        ↓
Background Worker consumes event
        ↓
Worker calls gRPC Recommendation Service
        ↓
System updates skill matrix and recommendations
        ↓
Learner receives personalized suggested lessons
```

## Project Goal

The goal of this project is to build a practical English learning system that satisfies the required technical components:

* REST API with .NET
* Background Job
* Kafka Message Broker
* Redis Cache
* gRPC Service
* Docker Deployment

## Status

Project is currently in the initial development phase.
