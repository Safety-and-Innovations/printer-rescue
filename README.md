# Printer Rescue

Restauração de estado funcional de impressoras Windows a partir de snapshot:
o app grava o estado que funcionava (IP, porta, protocolo, driver e versão,
fila, permissões, padrões) e o reconstrói quando o Windows quebra.

**REGRA Nº 1 do produto:** o Printer Rescue nunca hospeda, distribui, indexa
ou mantém driver de impressora. Ele restaura estado; não fornece binário.

## Estado

Projeto em inicialização — scaffold da solução e contratos congelados.
Ver `docs/CONTRATOS.md` para a especificação vigente dos módulos.

## Desenvolvimento

```bash
dotnet build PrinterRescue.sln       # zero warnings (TreatWarningsAsErrors)
dotnet test PrinterRescue.sln        # suíte completa
```

---
Criado por André Santo (forg3) | junkyardgoodies.app
