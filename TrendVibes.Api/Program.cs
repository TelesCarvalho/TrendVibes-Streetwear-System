using MySqlConnector;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTransient<MySqlConnection>(_ => 
    new MySqlConnection("Server=localhost;Database=trendvibesbd;Uid=root;Pwd=40365010;"));

// 1. Configurando o CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("PermitirFrontend", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();
app.UseCors("PermitirFrontend");

// ==========================================
// ROTA 1: PUXAR USUÁRIOS
// ==========================================
app.MapGet("/api/usuarios", async (IConfiguration config) =>
{
    var listaUsuarios = new List<object>();
    string stringConexao = config.GetConnectionString("TrendVibesDB")!;

    using (var conexao = new MySqlConnection(stringConexao))
    {
        await conexao.OpenAsync();
        using (var comando = new MySqlCommand("SELECT id, nome, email, status FROM Usuario", conexao))
        using (var reader = await comando.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                listaUsuarios.Add(new
                {
                    Id = reader.GetInt32("id"),
                    Nome = reader.GetString("nome"),
                    Email = reader.GetString("email"),
                    Status = reader.GetString("status")
                });
            }
        }
    }
    return Results.Ok(listaUsuarios);
});

// ==========================================
// ROTA 2: FAZER LOGIN 
// ==========================================
app.MapPost("/api/login", async ([FromBody] LoginRequest request, IConfiguration config) =>
{
    string stringConexao = config.GetConnectionString("TrendVibesDB")!;

    using (var conexao = new MySqlConnection(stringConexao))
    {
        await conexao.OpenAsync();

        string query = @"
            SELECT u.id AS usuarioId, col.id AS colaboradorId, u.nome, u.email, c.nome AS cargo, col.turnoStatus
            FROM Usuario u
            INNER JOIN Colaborador col ON u.id = col.usuarioId
            INNER JOIN Cargo c ON col.cargoId = c.id
            WHERE u.email = @email AND u.senha = @senha AND u.status = 'ATIVO'";
            
        using (var comando = new MySqlCommand(query, conexao))
        {
            comando.Parameters.AddWithValue("@email", request.Email);
            comando.Parameters.AddWithValue("@senha", request.Senha);

            using (var reader = await comando.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    string nomeCargo = reader.GetString("cargo");

                    if (nomeCargo != request.Cargo)
                    {
                        return Results.BadRequest(new { sucesso = false, mensagem = "O cargo selecionado não corresponde a este usuário." });
                    }

                    string urlDestino = nomeCargo switch
                    {
                        "Vendedor" => "vendedor.html",
                        "Estoquista" => "estoquista.html",
                        "Gerente" => "gerente.html",
                        "RH" => "rh.html",
                        _ => "login.html" 
                    };
                    
                    string statusBanco = reader.IsDBNull(reader.GetOrdinal("turnoStatus")) ? "AUSENTE" : reader.GetString(reader.GetOrdinal("turnoStatus"));

                    return Results.Ok(new { 
                        sucesso = true, 
                        colaboradorId = reader.GetInt32("colaboradorId"),
                        nome = reader.GetString("nome"), 
                        cargo = nomeCargo,
                        url = urlDestino,
                        email = reader.GetString("email"),
                        turnoStatus = statusBanco
                    });
                }
                else
                {
                    return Results.BadRequest(new { sucesso = false, mensagem = "E-mail ou senha incorretos." });
                }
            }
        }
    }
});

// ==========================================
// ROTA 3: REGISTRAR VENDA
// ==========================================
app.MapPost("/api/vendas", async ([FromBody] VendaRequest request, IConfiguration config) =>
{
    string stringConexao = config.GetConnectionString("TrendVibesDB")!;

    using (var conexao = new MySqlConnection(stringConexao))
    {
        await conexao.OpenAsync();

        // Aplicando a Regra de Negócio: Comissão de 5% sobre o valor da venda
        decimal comissaoCalculada = request.Valor * 0.05m;

        string query = @"
            INSERT INTO Venda (colaboradorId, sku, valor, comissao) 
            VALUES (@colaboradorId, @sku, @valor, @comissao)";

        using (var comando = new MySqlCommand(query, conexao))
        {
            comando.Parameters.AddWithValue("@colaboradorId", request.ColaboradorId);
            comando.Parameters.AddWithValue("@sku", request.Sku);
            comando.Parameters.AddWithValue("@valor", request.Valor);
            comando.Parameters.AddWithValue("@comissao", comissaoCalculada);

            await comando.ExecuteNonQueryAsync();

            return Results.Ok(new { 
                sucesso = true, 
                mensagem = "Venda registrada no banco de dados!", 
                comissao = comissaoCalculada 
            });
        }
    }
});

// ==========================================
// ROTA 4: PUXAR HISTÓRICO DE VENDAS DO COLABORADOR
// ==========================================
app.MapGet("/api/vendas/{colaboradorId}", async (int colaboradorId, IConfiguration config) =>
{
    var historico = new List<object>();
    string stringConexao = config.GetConnectionString("TrendVibesDB")!;

    using (var conexao = new MySqlConnection(stringConexao))
    {
        await conexao.OpenAsync();
        
        string query = @"
            SELECT sku, valor, comissao, DATE_FORMAT(dataHora, '%H:%i') as horaStr
            FROM Venda 
            WHERE colaboradorId = @id
            ORDER BY dataHora DESC 
            LIMIT 10"; 

        using (var comando = new MySqlCommand(query, conexao))
        {
            comando.Parameters.AddWithValue("@id", colaboradorId);

            using (var reader = await comando.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    historico.Add(new
                    {
                        sku = reader.GetString("sku"),
                        valor = reader.GetDecimal("valor"),
                        comissao = reader.GetDecimal("comissao"),
                        hora = reader.GetString("horaStr")
                    });
                }
            }
        }
    }
    return Results.Ok(historico);
});

// ==========================================
// ROTA 5: RESUMO DE METAS 
// ==========================================
app.MapGet("/api/vendas/resumo/{id}", async ([FromServices] MySqlConnection conexao, int id) => {
    try {
        await conexao.OpenAsync();
        
        string queryMeta = "SELECT valorMeta FROM MetasColaboradores WHERE colaboradorId = @id ORDER BY id DESC LIMIT 1";
        
        decimal metaDefinida = 25000.00m; 
        
        using (var cmd = new MySqlCommand(queryMeta, conexao)) {
            cmd.Parameters.AddWithValue("@id", id);
            var resultado = await cmd.ExecuteScalarAsync();
            
            if (resultado != null && resultado != DBNull.Value) {
                metaDefinida = Convert.ToDecimal(resultado);
            }
        }

        string queryVendas = "SELECT SUM(valor) FROM Venda WHERE colaboradorId = @id";
        decimal totalVendido = 0;
        using (var cmd = new MySqlCommand(queryVendas, conexao)) {
            cmd.Parameters.AddWithValue("@id", id);
            var resVendas = await cmd.ExecuteScalarAsync();
            if (resVendas != DBNull.Value) totalVendido = Convert.ToDecimal(resVendas);
        }

        return Results.Ok(new { 
            sucesso = true, 
            totalVendas = totalVendido, 
            metaMensal = metaDefinida,
            totalComissao = totalVendido * 0.05m 
        });
    } catch (Exception ex) {
        return Results.BadRequest(new { sucesso = false, mensagem = ex.Message });
    }
});

// ==========================================
// ROTA 6: RESUMO DE PROGRESSO DA EQUIPE DE VENDAS
// ==========================================
app.MapGet("/api/vendas/equipe/resumo", async ([FromServices] MySqlConnection conexao) => {
    var lista = new List<object>();
    await conexao.OpenAsync();
    
    // Busca todos os colaboradores que são "Vendedor" e "ATIVO", 
    // trazendo a meta mais recente e a soma total das vendas
    string query = @"
        SELECT 
            c.id AS colaboradorId, 
            u.nome, 
            COALESCE((SELECT valorMeta FROM MetasColaboradores m WHERE m.colaboradorId = c.id ORDER BY id DESC LIMIT 1), 25000.00) AS metaMensal,
            COALESCE((SELECT bonusPercentual FROM MetasColaboradores m WHERE m.colaboradorId = c.id ORDER BY id DESC LIMIT 1), 5.00) AS bonusPercentual,
            COALESCE((SELECT SUM(valor) FROM Venda v WHERE v.colaboradorId = c.id), 0) AS totalVendas
        FROM Colaborador c
        INNER JOIN Usuario u ON c.usuarioId = u.id
        INNER JOIN Cargo cg ON c.cargoId = cg.id
        WHERE cg.nome = 'Vendedor' AND u.status = 'ATIVO'";
    
    using (var comando = new MySqlCommand(query, conexao))
    using (var reader = await comando.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            lista.Add(new {
                colaboradorId = reader.GetInt32("colaboradorId"),
                nome = reader.GetString("nome"),
                metaMensal = reader.GetDecimal("metaMensal"),
                bonusPercentual = reader.GetDecimal("bonusPercentual"),
                totalVendas = reader.GetDecimal("totalVendas")
            });
        }
    }
    return Results.Ok(lista);
});

// 1. ROTA GET: Busca as tarefas para exibir na tela 
app.MapGet("/api/tarefas", async ([FromServices] MySqlConnection conexao) => {
    await conexao.OpenAsync();
    string query = "SELECT id, titulo, descricao, prioridade, status, concluidoPorNome, DATE_FORMAT(dataConclusao, '%H:%i') as horaConclusao FROM TarefaEstoque WHERE dataTarefa = CURRENT_DATE";
    
    using (var comando = new MySqlCommand(query, conexao))
    using (var reader = await comando.ExecuteReaderAsync())
    {
        var lista = new List<object>();
        while (await reader.ReadAsync())
        {
            lista.Add(new {
                id = reader.GetInt32("id"),
                titulo = reader.GetString("titulo"),
                descricao = reader.IsDBNull(2) ? "" : reader.GetString("descricao"),
                prioridade = reader.GetString("prioridade"),
                status = reader.GetBoolean("status"),
                concluidoPorNome = reader.IsDBNull(5) ? "" : reader.GetString("concluidoPorNome"),
                horaConclusao = reader.IsDBNull(6) ? "" : reader.GetString("horaConclusao")
            });
        }
        return Results.Ok(lista);
    }
});

// 2. ROTA PUT: Atualiza o status no banco salvando QUEM fez
app.MapPut("/api/tarefas/{id}", async ([FromServices] MySqlConnection conexao, int id, TarefaStatusRequest req) => {
    await conexao.OpenAsync();
    
    string query = req.Status 
        ? "UPDATE TarefaEstoque SET status = 1, concluidoPorNome = @nome, dataConclusao = NOW() WHERE id = @id"
        : "UPDATE TarefaEstoque SET status = 0, concluidoPorNome = NULL, dataConclusao = NULL WHERE id = @id";
    
    using (var comando = new MySqlCommand(query, conexao))
    {
        comando.Parameters.AddWithValue("@id", id);
        comando.Parameters.AddWithValue("@nome", req.NomeConcluinte);
        await comando.ExecuteNonQueryAsync();
    }
    return Results.Ok();
});

app.MapPost("/api/metas", async ([FromServices] MySqlConnection conexao, MetasRequest request) => {
    await conexao.OpenAsync();
    string query = @"INSERT INTO MetasColaboradores (colaboradorId, valorMeta, bonusPercentual, mesReferencia) 
                     VALUES (@cid, @meta, @bonus, @mes)
                     ON DUPLICATE KEY UPDATE valorMeta = @meta, bonusPercentual = @bonus";
    
    using (var comando = new MySqlCommand(query, conexao))
    {
        comando.Parameters.AddWithValue("@cid", request.ColaboradorId);
        comando.Parameters.AddWithValue("@meta", request.ValorMeta);
        comando.Parameters.AddWithValue("@bonus", request.bonusPercentual);
        comando.Parameters.AddWithValue("@mes", request.MesReferencia);
        await comando.ExecuteNonQueryAsync();
    }
    return Results.Ok(new { sucesso = true });
});

// ==========================================
// ROTA 7: ALTERAR STATUS DO TURNO (Todos os usuários)
// ==========================================
app.MapPut("/api/turno", async ([FromServices] MySqlConnection conexao, TurnoRequest req) => {
    await conexao.OpenAsync();
    
    // Se estiver ativando, registra horaEntrada. Se ficar ausente, registra horaSaida.
    string query = req.Status == "ATIVO" 
        ? "UPDATE Colaborador SET turnoStatus = 'ATIVO', horaEntrada = NOW() WHERE id = @id"
        : "UPDATE Colaborador SET turnoStatus = 'AUSENTE', horaSaida = NOW() WHERE id = @id";
    
    using (var comando = new MySqlCommand(query, conexao))
    {
        comando.Parameters.AddWithValue("@id", req.ColaboradorId);
        await comando.ExecuteNonQueryAsync();
    }
    return Results.Ok(new { sucesso = true });
});

// ==========================================
// ROTA 8: STATUS INTERNO DA EQUIPE (Painel do Gerente)
// ==========================================
app.MapGet("/api/equipe/status", async ([FromServices] MySqlConnection conexao) => {
    await conexao.OpenAsync();
    string query = @"
        SELECT c.id, u.nome, cg.nome as cargo, c.turnoStatus, 
               DATE_FORMAT(c.horaEntrada, '%H:%i') as horaEntrada,
               DATE_FORMAT(c.horaSaida, '%H:%i') as horaSaida
        FROM Colaborador c
        INNER JOIN Usuario u ON c.usuarioId = u.id
        INNER JOIN Cargo cg ON c.cargoId = cg.id
        WHERE u.status = 'ATIVO'";
    
    var lista = new List<object>();
    using (var comando = new MySqlCommand(query, conexao))
    using (var reader = await comando.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            lista.Add(new {
                id = reader.GetInt32("id"),
                nome = reader.GetString("nome"),
                cargo = reader.GetString("cargo"),
                turnoStatus = reader.IsDBNull(3) ? "AUSENTE" : reader.GetString("turnoStatus"),
                horaEntrada = reader.IsDBNull(4) ? "--:--" : reader.GetString("horaEntrada"),
                horaSaida = reader.IsDBNull(5) ? "--:--" : reader.GetString("horaSaida")
            });
        }
    }
    return Results.Ok(lista);
});

// ==========================================
// ROTA 9: CRIAR NOVA TAREFA (Painel do Gerente)
// ==========================================
app.MapPost("/api/tarefas", async ([FromServices] MySqlConnection conexao, TarefaRequest req) => {
    await conexao.OpenAsync();
    
    string query = @"INSERT INTO TarefaEstoque (titulo, descricao, prioridade, dataTarefa, status) 
                     VALUES (@titulo, @descricao, @prioridade, @dataTarefa, 0)";
                     
    using (var comando = new MySqlCommand(query, conexao))
    {
        comando.Parameters.AddWithValue("@titulo", req.Titulo);
        comando.Parameters.AddWithValue("@descricao", req.Descricao);
        comando.Parameters.AddWithValue("@prioridade", req.Prioridade);
        comando.Parameters.AddWithValue("@dataTarefa", req.DataTarefa.ToString("yyyy-MM-dd"));
        
        await comando.ExecuteNonQueryAsync();
    }
    return Results.Ok(new { sucesso = true });
});

// ==========================================
// ROTA 10: LISTAR COLABORADORES PARA O RH (Dropdown)
// ==========================================
app.MapGet("/api/colaboradores/simples", async ([FromServices] MySqlConnection conexao) => {
    await conexao.OpenAsync();
    string query = @"
        SELECT c.id, u.nome, cg.nome as cargo 
        FROM Colaborador c
        INNER JOIN Usuario u ON c.usuarioId = u.id
        INNER JOIN Cargo cg ON c.cargoId = cg.id
        WHERE u.status = 'ATIVO'";
    
    var lista = new List<object>();
    using (var comando = new MySqlCommand(query, conexao))
    using (var reader = await comando.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            lista.Add(new {
                id = reader.GetInt32("id"),
                nome = reader.GetString("nome"),
                cargo = reader.GetString("cargo")
            });
        }
    }
    return Results.Ok(lista);
});

// ==========================================
// ROTA 11: GERAR DADOS DO HOLERITE 
// ==========================================
app.MapGet("/api/holerite/{id}", async ([FromServices] MySqlConnection conexao, int id) => {
    await conexao.OpenAsync();
    
    string nome = "";
    string cargo = "";
    decimal salarioBase = 1800.00m; 
    string dataAdmissao = "15/02/2024"; 
    string cbo = "5211-10"; 
    
    // 1. Puxa os dados do colaborador
    string queryFunc = @"
        SELECT u.nome, cg.nome as cargo 
        FROM Colaborador c
        INNER JOIN Usuario u ON c.usuarioId = u.id
        INNER JOIN Cargo cg ON c.cargoId = cg.id
        WHERE c.id = @id";
        
    using (var cmd = new MySqlCommand(queryFunc, conexao)) {
        cmd.Parameters.AddWithValue("@id", id);
        using (var reader = await cmd.ExecuteReaderAsync()) {
            if (await reader.ReadAsync()) {
                nome = reader.GetString("nome");
                cargo = reader.GetString("cargo");
                
                if(cargo == "Gerente") { salarioBase = 3500.00m; cbo = "1414-10"; }
                else if(cargo == "Estoquista") { salarioBase = 1600.00m; cbo = "4141-05"; }
                else if(cargo == "RH") { salarioBase = 2500.00m; cbo = "4110-05"; }
            }
        }
    }

    // 2. Puxa as Comissões
    decimal totalVendas = 0;
    decimal comissao = 0;
    string queryVendas = "SELECT SUM(valor), SUM(comissao) FROM Venda WHERE colaboradorId = @id";
    using (var cmd = new MySqlCommand(queryVendas, conexao)) {
        cmd.Parameters.AddWithValue("@id", id);
        using (var reader = await cmd.ExecuteReaderAsync()) {
            if (await reader.ReadAsync() && !reader.IsDBNull(0)) {
                totalVendas = reader.GetDecimal(0);
                comissao = reader.GetDecimal(1);
            }
        }
    }

    // 3. CONTA AS FALTAS NÃO JUSTIFICADAS DO MÊS ATUAL
    int diasFalta = 0;
    string queryFaltas = @"SELECT COUNT(*) FROM Ocorrencia 
                           WHERE colaboradorId = @id AND tipo = 'Falta Não Justificada' 
                           AND MONTH(dataOcorrencia) = MONTH(CURRENT_DATE) 
                           AND YEAR(dataOcorrencia) = YEAR(CURRENT_DATE)";
    using (var cmd = new MySqlCommand(queryFaltas, conexao)) {
        cmd.Parameters.AddWithValue("@id", id);
        diasFalta = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // 4. Cálculos do Holerite (Matemática da Folha)
    decimal totalVencimentos = salarioBase + comissao;
    decimal inss = Math.Round(totalVencimentos * 0.0806m, 2); 
    
    // Calcula o desconto: (Salário Base / 30 dias) * quantidade de faltas
    decimal faltasValor = Math.Round((salarioBase / 30m) * diasFalta, 2); 
    
    decimal totalDescontos = inss + faltasValor;
    decimal liquido = totalVencimentos - totalDescontos;

    return Results.Ok(new {
        sucesso = true,
        nome = nome,
        cargo = cargo,
        admissao = dataAdmissao,
        cbo = cbo,
        salarioBase = salarioBase,
        totalVendas = totalVendas,
        comissao = comissao,
        inss = inss,
        diasFalta = diasFalta,
        faltas = faltasValor,
        totalVencimentos = totalVencimentos,
        totalDescontos = totalDescontos,
        liquido = liquido
    });
});

// ==========================================
// ROTA 12: REGISTRAR OCORRÊNCIA / FALTA (RH)
// ==========================================
app.MapPost("/api/ocorrencias", async ([FromServices] MySqlConnection conexao, OcorrenciaRequest req) => {
    await conexao.OpenAsync();
    string query = @"INSERT INTO Ocorrencia (colaboradorId, dataOcorrencia, tipo, anexo) 
                     VALUES (@cid, @data, @tipo, @anexo)";
    using (var cmd = new MySqlCommand(query, conexao)) {
        cmd.Parameters.AddWithValue("@cid", req.ColaboradorId);
        cmd.Parameters.AddWithValue("@data", req.DataOcorrencia);
        cmd.Parameters.AddWithValue("@tipo", req.Tipo);
        cmd.Parameters.AddWithValue("@anexo", req.Anexo);
        await cmd.ExecuteNonQueryAsync();
    }
    return Results.Ok(new { sucesso = true });
});

// ==========================================
// ROTA 13: LISTAR TODOS OS COLABORADORES
// ==========================================
app.MapGet("/api/pessoal", async ([FromServices] MySqlConnection conexao) => {
    await conexao.OpenAsync();
    string query = @"
        SELECT c.id as colabId, u.id as userId, u.nome, u.email, cg.nome as cargo, u.status
        FROM Colaborador c
        INNER JOIN Usuario u ON c.usuarioId = u.id
        INNER JOIN Cargo cg ON c.cargoId = cg.id
        ORDER BY u.status ASC, u.nome ASC";

    var lista = new List<object>();
    using (var cmd = new MySqlCommand(query, conexao))
    using (var reader = await cmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            lista.Add(new {
                colabId = reader.GetInt32(0),
                userId = reader.GetInt32(1),
                nome = reader.IsDBNull(2) ? "SEM NOME" : reader.GetString(2),
                email = reader.IsDBNull(3) ? "SEM E-MAIL" : reader.GetString(3),
                cargo = reader.IsDBNull(4) ? "SEM CARGO" : reader.GetString(4),
                status = reader.IsDBNull(5) ? "INATIVO" : reader.GetString(5)
            });
        }
    }
    return Results.Ok(lista);
});

// ==========================================
// ROTA 14: SALVAR NOVO OU EDITAR FUNCIONÁRIO 
// ==========================================
app.MapPost("/api/pessoal", async ([FromServices] MySqlConnection conexao, FuncionarioRequest req) => {
    await conexao.OpenAsync();
    using var transaction = await conexao.BeginTransactionAsync();
    try {
        var gatilhos = new List<string>();
        using (var cmdBusca = new MySqlCommand("SELECT TRIGGER_NAME FROM information_schema.triggers WHERE LOWER(EVENT_OBJECT_TABLE) IN ('usuario', 'colaborador')", conexao, transaction))
        using (var reader = await cmdBusca.ExecuteReaderAsync()) {
            while (await reader.ReadAsync()) gatilhos.Add(reader.GetString(0));
        }
        foreach(var g in gatilhos) {
            using (var cmdDrop = new MySqlCommand($"DROP TRIGGER IF EXISTS `{g}`", conexao, transaction)) {
                await cmdDrop.ExecuteNonQueryAsync();
            }
        }

        int cargoId = 1;
        string nomeBusca = req.Cargo == "RH" ? "Recursos Humanos" : req.Cargo;
        
        using (var cmdCargo = new MySqlCommand("SELECT id FROM Cargo WHERE nome = @nome LIMIT 1", conexao, transaction)) {
            cmdCargo.Parameters.AddWithValue("@nome", nomeBusca);
            var res = await cmdCargo.ExecuteScalarAsync();
            if(res != null) {
                cargoId = Convert.ToInt32(res);
            } else {
                using (var cmdFallback = new MySqlCommand("SELECT id FROM Cargo LIMIT 1", conexao, transaction)) {
                    var fallbackRes = await cmdFallback.ExecuteScalarAsync();
                    if (fallbackRes != null) cargoId = Convert.ToInt32(fallbackRes);
                }
            }
        }

        if (req.UserId == 0) {
            string qUser = "INSERT INTO Usuario (nome, email, senha, status) VALUES (@n, @e, @s, 'ATIVO')";
            int newUserId = 0;
            using (var cmdU = new MySqlCommand(qUser, conexao, transaction)) {
                cmdU.Parameters.AddWithValue("@n", req.Nome);
                cmdU.Parameters.AddWithValue("@e", req.Email);
                cmdU.Parameters.AddWithValue("@s", req.Senha);
                await cmdU.ExecuteNonQueryAsync();
                newUserId = Convert.ToInt32(cmdU.LastInsertedId);
            }
            
            string qColab = "INSERT INTO Colaborador (usuarioId, cargoId, dataAdmissao, salarioBase) VALUES (@u, @c, @da, @sb)";
            using (var cmdC = new MySqlCommand(qColab, conexao, transaction)) {
                cmdC.Parameters.AddWithValue("@u", newUserId);
                cmdC.Parameters.AddWithValue("@c", cargoId);
                cmdC.Parameters.AddWithValue("@da", string.IsNullOrEmpty(req.DataAdmissao) ? DateTime.Now.ToString("yyyy-MM-dd") : req.DataAdmissao);
                cmdC.Parameters.AddWithValue("@sb", req.SalarioBase);
                await cmdC.ExecuteNonQueryAsync();
            }
        } else {
            string qUser = "UPDATE Usuario SET nome = @n, email = @e" +
                           (string.IsNullOrEmpty(req.Senha) ? "" : ", senha = @s") + 
                           " WHERE id = @uid";
            using (var cmdU = new MySqlCommand(qUser, conexao, transaction)) {
                cmdU.Parameters.AddWithValue("@n", req.Nome);
                cmdU.Parameters.AddWithValue("@e", req.Email);
                cmdU.Parameters.AddWithValue("@uid", req.UserId);
                if(!string.IsNullOrEmpty(req.Senha)) cmdU.Parameters.AddWithValue("@s", req.Senha);
                await cmdU.ExecuteNonQueryAsync();
            }
            string qColab = "UPDATE Colaborador SET cargoId = @c, salarioBase = @sb WHERE id = @cid";
            using (var cmdC = new MySqlCommand(qColab, conexao, transaction)) {
                cmdC.Parameters.AddWithValue("@c", cargoId);
                cmdC.Parameters.AddWithValue("@sb", req.SalarioBase); 
                cmdC.Parameters.AddWithValue("@cid", req.ColabId);
                await cmdC.ExecuteNonQueryAsync();
            }
        }
        await transaction.CommitAsync();
        return Results.Ok(new { sucesso = true });
    } catch (Exception ex) {
        await transaction.RollbackAsync();
        Console.WriteLine("ERRO NO CRUD DE USUÁRIO: " + ex.Message);
        return Results.BadRequest(new { sucesso = false, mensagem = ex.Message });
    }
});

// ==========================================
// ROTA 15: ALTERAR STATUS (INATIVAR / REATIVAR)
// ==========================================
app.MapPut("/api/pessoal/status", async ([FromServices] MySqlConnection conexao, StatusRequest req) => {
    await conexao.OpenAsync();
    string query = "UPDATE Usuario SET status = @status WHERE id = @uid";
    using (var cmd = new MySqlCommand(query, conexao)) {
        cmd.Parameters.AddWithValue("@status", req.NovoStatus);
        cmd.Parameters.AddWithValue("@uid", req.UserId);
        await cmd.ExecuteNonQueryAsync();
    }
    return Results.Ok(new { sucesso = true });
});

// ==========================================
// ROTA 16: CÁLCULO DE TICKET MÉDIO 
// ==========================================
app.MapGet("/api/ticket/{id}", async ([FromServices] MySqlConnection conexao, int id) => {
    await conexao.OpenAsync();
    
    string cargo = "";
    using (var cmdCargo = new MySqlCommand("SELECT cg.nome FROM Colaborador c INNER JOIN Cargo cg ON c.cargoId = cg.id WHERE c.id = @id", conexao)) {
        cmdCargo.Parameters.AddWithValue("@id", id);
        var result = await cmdCargo.ExecuteScalarAsync();
        
        if (result != null) cargo = result.ToString() ?? ""; 
    }

    string queryVendas = cargo == "Gerente" 
        ? "SELECT COUNT(*), SUM(valor) FROM Venda" 
        : "SELECT COUNT(*), SUM(valor) FROM Venda WHERE colaboradorId = @id";

    using (var cmd = new MySqlCommand(queryVendas, conexao)) {
        if (cargo != "Gerente") cmd.Parameters.AddWithValue("@id", id);
        
        using (var reader = await cmd.ExecuteReaderAsync()) {
            if (await reader.ReadAsync()) {
                int qtdVendas = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                decimal somaVendas = reader.IsDBNull(1) ? 0 : reader.GetDecimal(1);
                
                decimal ticketMedio = qtdVendas > 0 ? somaVendas / qtdVendas : 0;
                
                return Results.Ok(new { sucesso = true, ticket = ticketMedio });
            }
        }
    }
    return Results.Ok(new { sucesso = true, ticket = 0 });
});

app.MapGet("/api/limpar-gatilhos", async ([FromServices] MySqlConnection conexao) => {
    await conexao.OpenAsync();
    
    string queryBusca = "SELECT TRIGGER_NAME FROM information_schema.triggers WHERE EVENT_OBJECT_TABLE = 'Usuario' AND ACTION_TIMING = 'AFTER' AND EVENT_MANIPULATION = 'INSERT'";
    
    var nomesGatilhos = new List<string>();
    using (var cmd = new MySqlCommand(queryBusca, conexao)) 
    using (var reader = await cmd.ExecuteReaderAsync()) {
        while (await reader.ReadAsync()) {
            nomesGatilhos.Add(reader.GetString(0));
        }
    }

    if (nomesGatilhos.Count > 0) {
        foreach(var nome in nomesGatilhos) {
            using (var cmdDrop = new MySqlCommand($"DROP TRIGGER IF EXISTS `{nome}`", conexao)) {
                await cmdDrop.ExecuteNonQueryAsync();
            }
        }
        return $"Magia feita! Apagamos o(s) gatilho(s) problemático(s): {string.Join(", ", nomesGatilhos)}. Volte ao painel do RH e cadastre o usuário!";
    }
    
    return "Nenhum gatilho encontrado. Se o erro persistir, me avise!";
});


app.Run();

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
}

public class VendaRequest
{
    public int ColaboradorId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public decimal Valor { get; set; }
}

public class MetasRequest
{
    public int ColaboradorId { get; set; }
    public decimal ValorMeta { get; set; }
    public decimal bonusPercentual { get; set; } 
    public string MesReferencia { get; set; } = string.Empty;
}

public class TurnoRequest {
    public int ColaboradorId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class TarefaRequest
{
    public string Titulo { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string Prioridade { get; set; } = string.Empty;
    public DateTime DataTarefa { get; set; }
}

public class TarefaStatusRequest
{
    public bool Status { get; set; }
    public string NomeConcluinte { get; set; } = string.Empty;
}

public class OcorrenciaRequest {
    public int ColaboradorId { get; set; }
    public string DataOcorrencia { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public string Anexo { get; set; } = string.Empty;
}

public class FuncionarioRequest {
    public int UserId { get; set; }
    public int ColabId { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Senha { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    
    public string DataAdmissao { get; set; } = string.Empty; 
    public decimal SalarioBase { get; set; } 
}

public class StatusRequest {
    public int UserId { get; set; }
    public string NovoStatus { get; set; } = string.Empty;
}