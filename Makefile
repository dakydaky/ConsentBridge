.PHONY: up down logs seed migrate add-migration

up:
	docker compose up --build -d

logs:
	docker compose logs -f gateway mockboard agentconsole postgres

down:
	docker compose down -v

add-migration:
	dotnet tool restore || true
	dotnet ef migrations add Initial --project src/Gateway.Infrastructure --startup-project src/Gateway.Api

migrate:
	dotnet ef database update --project src/Gateway.Infrastructure --startup-project src/Gateway.Api
