using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Questions;

namespace CoreLearningSystem.API.Controllers;

[Authorize(Roles = "Admin")]
public class QuestionsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<QuestionDetailDto>>>> GetAll()
    {
        var result = await Mediator.Send(new GetQuestionsQuery());
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<QuestionDetailDto>>> Create([FromBody] CreateQuestionCommand command)
    {
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<QuestionDetailDto>>> Update(int id, [FromBody] UpdateQuestionCommand command)
    {
        if (id != command.Id) return BadRequest(ApiResponse<QuestionDetailDto>.FailureResponse("Mismatched Question ID."));
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int id)
    {
        var result = await Mediator.Send(new DeleteQuestionCommand(id));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }
}
