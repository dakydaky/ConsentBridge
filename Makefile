.PHONY: up down logs seed migrate add-migration

up:
	docker compose up --build -d

up-nc:
	docker compose build --no-cache
	docker compose up -d

logs:
	docker compose logs -f gateway mockboard agentconsole postgres

logs-save:
	@mkdir -p logs
	@echo "Saving compose logs with timestamps …"
	@docker compose logs --no-color --timestamps > logs/compose-`date -u +%Y%m%d-%H%M%S`.log

.PHONY: dev-clean dev-clean-hard prune-dangling builder-prune volumes-prune docker-nuke

# Stop and remove this project's containers, networks; keep named volumes
dev-clean:
	docker compose down --remove-orphans

# Stop and remove containers, networks, anonymous volumes, and images built by compose
dev-clean-hard:
	docker compose down -v --rmi local --remove-orphans
	docker builder prune -f

# Remove dangling images only (no longer referenced)
prune-dangling:
	docker image prune -f

# Remove build cache
builder-prune:
	docker builder prune -f

# Remove unused volumes
volumes-prune:
	docker volume prune -f

# NUKE: remove ALL unused data, including images, containers, networks, and volumes
# Equivalent to: docker system prune -a --volumes -f
docker-nuke:
	docker system prune -a --volumes -f

.PHONY: migrate-run
migrate-run:
	docker compose run --rm migrator

down:
	docker compose down -v

add-migration:
	@echo Usage: make add-migration NAME=DescriptiveName  or  make add-migration-DescriptiveName
	@exit 1

# Alternate syntax without NAME var, e.g.:
#   make add-migration-AddAuditTables
add-migration-%:
	dotnet ef migrations add $* --project src/Gateway.Infrastructure --startup-project src/Gateway.Api

migrate:
	dotnet ef database update --project src/Gateway.Infrastructure --startup-project src/Gateway.Api

.PHONY: migrations-list
migrations-list:
	dotnet ef migrations list --project src/Gateway.Infrastructure --startup-project src/Gateway.Api
