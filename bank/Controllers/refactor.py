import re

with open("TransactionController.cs", "r", encoding="utf-8") as f:
    content = f.read()

# 1. Add AML check in Transfer method
aml_check_code = """
            var amlService = HttpContext.RequestServices.GetRequiredService<bank.Services.AmlService>();
            if (amlService.IsTransactionSuspicious(request))
            {
                var amlTx = new Transaction
                {
                    Id = Guid.NewGuid(),
                    FromAccountId = sourceAccount.Id,
                    Amount = request.Amount,
                    Description = !string.IsNullOrEmpty(request.Description) ? $"AML HOLD: {request.Description}" : "AML HOLD Transfer",
                    Status = "AML_HOLD",
                    Timestamp = DateTime.UtcNow,
                    TransactionType = request.TransactionType,
                    TransferRequestJson = System.Text.Json.JsonSerializer.Serialize(request)
                };
                _context.Transactions.Add(amlTx);
                await _context.SaveChangesAsync();
                return Ok(new { Message = "Transaction blocked by AML. Please provide an explanation.", AmlHold = true, TransactionId = amlTx.Id });
            }
"""

# Find where to insert AML check
insert_idx = content.find("using var transaction = await _context.Database.BeginTransactionAsync();")
if insert_idx != -1:
    content = content[:insert_idx] + aml_check_code + "\n            " + content[insert_idx:]

# 2. Extract execution logic into ExecuteTransferInternalAsync
# Find the method boundary of Transfer
method_end_idx = content.find("        [HttpGet(\"history\")]")
# We will create a new method above history that takes (TransferRequestDto request, Account sourceAccount, decimal feeAmount, bank.Services.ExternalPayments.AchService achService, bank.Services.ExternalPayments.FedNowService fedNowService, bank.Services.ExternalPayments.RtpService rtpService)

execution_code = """
        private async Task<IActionResult> ExecuteTransferInternalAsync(
            TransferRequestDto request, 
            Account sourceAccount, 
            decimal feeAmount,
            bank.Services.ExternalPayments.AchService achService,
            bank.Services.ExternalPayments.FedNowService fedNowService,
            bank.Services.ExternalPayments.RtpService rtpService)
        {
"""

# Find the block starting from using var transaction to await transaction.CommitAsync();
# It is between insert_idx (which moved due to aml check insertion)
# Wait, it's easier to find the exact strings
start_exec = "using var transaction = await _context.Database.BeginTransactionAsync();"
end_exec = "await transaction.CommitAsync();"
start_idx = content.find(start_exec)
end_idx = content.find(end_exec, start_idx) + len(end_exec)

extracted_block = content[start_idx:end_idx]

# Replace the extracted block with a call to the new method in Transfer
call_code = """
            return await ExecuteTransferInternalAsync(request, sourceAccount, feeAmount, achService, fedNowService, rtpService);
"""

new_method = execution_code + extracted_block + "\n\n            return Ok(new { Message = \"Transfer successful.\" });\n        }\n\n"

content = content[:start_idx] + call_code + content[end_idx:]

# But wait, in Transfer method there is "return Ok(new { Message = \"Transfer successful.\" });" after CommitAsync(). We need to remove it from Transfer and put it in new method.
# Let's clean up Transfer:
content = content.replace("            return await ExecuteTransferInternalAsync(request, sourceAccount, feeAmount, achService, fedNowService, rtpService);\n\n            return Ok(new { Message = \"Transfer successful.\" });", "            return await ExecuteTransferInternalAsync(request, sourceAccount, feeAmount, achService, fedNowService, rtpService);")

# Insert new_method before [HttpGet("history")]
hist_idx = content.find("        [HttpGet(\"history\")]")
content = content[:hist_idx] + new_method + content[hist_idx:]

# 3. Add AML Endpoints
aml_endpoints = """
        [HttpPost("explain-aml/{id}")]
        public async Task<IActionResult> ExplainAml(Guid id, [FromBody] string explanation)
        {
            var tx = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.Status == "AML_HOLD");
            if (tx == null) return NotFound(new { Message = "Transaction not found or not in AML hold." });
            
            tx.AmlExplanation = explanation;
            tx.Status = "AML_EXPLAINED";
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Explanation submitted." });
        }

        [HttpPost("approve-aml/{id}")]
        [Authorize(Roles = "Employee,Admin")]
        public async Task<IActionResult> ApproveAml(Guid id, 
            [FromServices] bank.Services.ExternalPayments.AchService achService,
            [FromServices] bank.Services.ExternalPayments.FedNowService fedNowService,
            [FromServices] bank.Services.ExternalPayments.RtpService rtpService)
        {
            var tx = await _context.Transactions
                .Include(t => t.FromAccount)
                .FirstOrDefaultAsync(t => t.Id == id && t.Status == "AML_EXPLAINED");
                
            if (tx == null || tx.FromAccount == null) return NotFound(new { Message = "Transaction not found." });
            
            var dto = System.Text.Json.JsonSerializer.Deserialize<TransferRequestDto>(tx.TransferRequestJson ?? "{}");
            if (dto == null) return BadRequest(new { Message = "Invalid request data." });

            // Calculate fees (copied from Transfer)
            decimal feeAmount = 0;
            if (dto.TransactionType == "FedNow") feeAmount = 0.50m;
            else if (dto.TransactionType == "SWIFT") 
            {
                if (dto.ChargeBearer == "DEBT" || dto.ChargeBearer == "SHAR")
                    feeAmount = Math.Round(dto.Amount * 0.0035m, 2);
            }

            if (tx.FromAccount.Balance < (dto.Amount + feeAmount))
                return BadRequest(new { Message = "Insufficient funds." });

            var result = await ExecuteTransferInternalAsync(dto, tx.FromAccount, feeAmount, achService, fedNowService, rtpService);
            
            // Mark the original AML transaction as completed (to avoid duplicate history entries)
            // ExecuteTransferInternalAsync created NEW transactions. 
            // So we delete the old AML placeholder.
            _context.Transactions.Remove(tx);
            await _context.SaveChangesAsync();

            return result;
        }

        [HttpPost("reject-aml/{id}")]
        [Authorize(Roles = "Employee,Admin")]
        public async Task<IActionResult> RejectAml(Guid id)
        {
            var tx = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.Status == "AML_EXPLAINED");
            if (tx == null) return NotFound(new { Message = "Transaction not found." });
            
            tx.Status = "Failed";
            await _context.SaveChangesAsync();
            return Ok(new { Message = "Transaction rejected." });
        }

"""

end_class_idx = content.rfind("    }")
content = content[:end_class_idx] + aml_endpoints + content[end_class_idx:]

with open("TransactionController.cs", "w", encoding="utf-8") as f:
    f.write(content)
