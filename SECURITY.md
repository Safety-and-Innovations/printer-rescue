# Política de Segurança — Printer Rescue

## Versões suportadas

| Versão | Suportada |
|---|---|
| 1.0.x | ✅ |

## Reportando vulnerabilidade

**NÃO abra issue pública.** Envie relatório para o contato de segurança do
repositório (GitHub Security Advisories → "Report a vulnerability") ou pelo
canal publicado em junkyardgoodies.app.

Inclua: versão afetada, passos de reprodução, impacto estimado, prova de
conceito se houver. Resposta em até 72 h; correção alvo em 30 dias para
vulnerabilidades de alta severidade.

## Superfície de ataque e decisões de projeto relevantes

- **Sem telemetria, sem rede própria**: o app só fala com o subsistema de
  impressão local e com a impressora alvo (TCP 9100/515/631 quando aplicável).
- **Snapshots locais**: ficam em `%ProgramData%\PrinterRescue\snapshots\`,
  leitura/escrita restrita ao perfil administrativo da máquina.
- **Parser endurecido**: JSON com profundidade máxima 16, tamanho máximo 1 MiB,
  caminhos validados contra traversal — snapshot é dado não confiável ao ser
  recarregado.
- **REGRA Nº 1 como defesa estrutural**: por nunca baixar nem distribuir
  drivers, o app não é veículo de supply chain de binário de terceiros.
- **Elevação mínima**: operações destrutivas exigem admin explícito; a GUI roda
  `asInvoker` e eleva apenas no reparo.

## Escopo fora de garantia

Impressoras que nunca funcionaram na máquina-alvo estão fora do escopo do
produto por decisão de projeto (ver README § REGRA Nº 1).
