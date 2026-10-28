using Contracts;
namespace WebMVC.Models;

public sealed record LearningPathStepViewModel(ModuleStepDto Step)
{
    public string Heading => Step.ItemType == PathModuleItemTypeDto.AiTask ? "AI-graded task" : Step.ItemType == PathModuleItemTypeDto.MockExam ? "Mock exam assessment" : "Mentor session";
    public string Description => Step.ItemType == PathModuleItemTypeDto.AiTask ? "AI writing/speaking task · graded with notes per criterion" : "Full timed mock exam";
}
