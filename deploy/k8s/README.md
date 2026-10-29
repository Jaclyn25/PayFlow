# PayFlow Kubernetes Deployment Guide

This directory contains production-ready Kubernetes manifests for deploying the PayFlow financial platform with high availability, zero-downtime rolling updates, and horizontal auto-scaling.

## Architecture & Guarantees

- **High Availability**: Default 3 replicas distributed across nodes with PodDisruptionBudget compatibility.
- **Zero-Downtime Rolling Updates**:
  - `maxSurge: 1` and `maxUnavailable: 0` ensures existing traffic is served without dropping requests.
  - `preStop: exec: ["/bin/sleep", "5"]` hook allows ingress and endpoint controllers to withdraw the pod from service before application termination.
- **Health Checks & Probes**:
  - `startupProbe`: Mapped to `/liveness` with 150s failure window to accommodate cold starts and database migrations.
  - `livenessProbe`: Mapped to `/liveness` to detect deadlock or unresponsive process threads.
  - `readinessProbe`: Mapped to `/readiness` to prevent routing traffic before database and cache dependencies are verified.
- **Horizontal Pod Autoscaling**:
  - Automatically scales between 2 and 10 replicas based on CPU (70%) and Memory (80%) thresholds.
- **Prometheus Metrics**:
  - Standard annotations enabled for Prometheus scraping on `/metrics` at port 8080.
- **Ingress & TLS**:
  - Configured for NGINX Ingress controller with WebSocket support for live SignalR `/hubs/wallet` notifications and cert-manager Let's Encrypt TLS.

## Deployment with Kustomize

### 1. Apply to Cluster
```bash
# Preview manifests
kubectl kustomize deploy/k8s/

# Apply to cluster
kubectl apply -k deploy/k8s/
```

### 2. Verify Deployment Status
```bash
# Check namespace pods
kubectl get pods -n payflow

# Verify service and endpoints
kubectl get svc,endpoints -n payflow

# Check HPA status
kubectl get hpa -n payflow

# Inspect logs
kubectl logs -n payflow -l app.kubernetes.io/name=payflow-api -f
```
