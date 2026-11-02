using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace WorldWeaver.Editor.Assistant
{
    /// <summary>Read-only task-card presentation. A proposed plan is not an executed or verified task.</summary>
    public sealed class ChatTaskPresentation
    {
        public readonly bool Visible;
        public readonly string CssClass, Title, Status, Detail;

        ChatTaskPresentation(bool visible, string cssClass, string title, string status, string detail = "")
        { Visible = visible; CssClass = cssClass; Title = title; Status = status; Detail = detail; }

        // Include the displayed outcome as well as the task: failures/cancellation need not mutate Task.Status.
        public string Signature(AgentTaskState task) => JsonConvert.SerializeObject(new { Task = task, Presentation = this });

        static string Normalize(string value) => (value ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');
        static bool Is(string value, params string[] states) => states.Contains(value, StringComparer.Ordinal);

        public static ChatTaskPresentation From(AssistantMessage message, bool operationActive = false)
        {
            var task = message?.Task;
            var checks = (task?.Checks ?? new List<AgentCheckResult>()).Where(check => check != null).ToArray();
            var requirements = (task?.Requirements ?? new List<AgentRequirement>()).Where(item => item != null).ToArray();
            bool pendingJob = !string.IsNullOrEmpty(task?.PendingJobId);
            bool visible = task != null && (task.MutationAuthorized || requirements.Length > 0 || checks.Length > 0 || pendingJob);
            if (!visible) return new ChatTaskPresentation(false, "task-card-neutral", "", "");

            string outcome = Normalize(message.Status), state = Normalize(task.Status);
            bool hasResults = message.Steps?.Any(step => step != null && step.Success) == true;
            string priorResults = hasResults ? " Уже выполненные действия перечислены ниже." : "";
            if (state == "restored" && !message.IsError)
                return new ChatTaskPresentation(true, "task-card-neutral", "Изменения отменены", "Состояние восстановлено");
            if (Is(outcome, "cancelled", "canceled", "остановлено", "запрос отменен", "отменено", "продолжение остановлено") || Is(state, "cancelled", "canceled"))
                return new ChatTaskPresentation(true, "task-card-pending", "Запрос остановлен", "Выполнение задачи не завершено", "Запрос отменён. Успешное завершение не подтверждено." + priorResults);
            if (Is(outcome, "timeout", "timed_out", "deadline_exceeded", "лимит времени", "время ожидания истекло") || Is(state, "timeout", "timed_out"))
                return new ChatTaskPresentation(true, "task-card-error", "Лимит времени", "Выполнение задачи не завершено", "Время ожидания истекло. Результат задачи не подтверждён." + priorResults);
            if (message.IsError || Is(outcome, "error", "failed", "не выполнено", "не применено", "ошибка применения", "проверки не пройдены", "проверка не найдена") || Is(state, "error", "failed"))
                return new ChatTaskPresentation(true, "task-card-error", "Задача не выполнена", "Запрос завершился с ошибкой", "Успешное выполнение задачи не подтверждено." + priorResults);
            if (message.PendingPlan != null)
                return new ChatTaskPresentation(true, "task-card-pending", "План ожидает применения", "Этот план ещё не применён", "Предложение подготовлено для просмотра. После применения появятся результаты проверок." + priorResults);
            if (pendingJob || Is(state, "waiting_compile", "waiting_compilation", "compiling", "waiting_job", "waiting_editor_job", "job_pending"))
                return new ChatTaskPresentation(true, "task-card-pending", "Проверка ещё не завершена", "Ожидаю результат Unity", "Готовность задачи пока не подтверждена.");
            if (operationActive)
                return new ChatTaskPresentation(true, "task-card-pending", "Задача выполняется", "Работа продолжается", "Результат будет показан после завершения и проверок.");
            if (state=="blocked"||state=="partial")
                return new ChatTaskPresentation(true,"task-card-pending",state=="partial"?"Выполнено частично":"Не хватает ресурса или доступа","Полное выполнение не подтверждено",string.Join("; ",task.OpenQuestions??new List<string>())+priorResults);
            if (task.ContinueAfterReload || Is(state, "waiting_continuation", "interrupted") || Is(outcome, "нужно продолжить вручную", "нужна проверка", "нужно уточнение"))
                return new ChatTaskPresentation(true, "task-card-pending", "Задача не завершена", "Ожидает продолжения", "Продолжите задачу или проверьте уже выполненные действия.");
            if (checks.Any(check => !check.Passed) || state == "checks_failed")
                return new ChatTaskPresentation(true, "task-card-error", "Проверки не пройдены", "Часть критериев не подтверждена", "Результаты отдельных проверок приведены ниже.");
            if (task.OpenQuestions?.Any(question => !string.IsNullOrWhiteSpace(question)) == true)
                return new ChatTaskPresentation(true, "task-card-pending", "Нужно уточнение", "Результат задачи не подтверждён", "Уточните открытые вопросы перед итоговой проверкой.");

            bool allRequirementsChecked = requirements.All(requirement => !string.IsNullOrEmpty(requirement.Id) && checks.Any(check => check.Passed && check.RequirementId == requirement.Id));
            bool terminal = Is(outcome, "done", "complete", "completed", "готово", "проверки пройдены", "изменения применены", "шаг выполнен", "checks_passed", "literal_checks_passed");
            if (checks.Length > 0 && allRequirementsChecked && terminal && Is(state, "checks_passed", "literal_checks_passed"))
                return new ChatTaskPresentation(true, requirements.Any(r=>r.Kind=="task_change")?"task-card-neutral":"task-card-success", "Проверено " + checks.Length + " из " + checks.Length,
                    requirements.Any(r=>r.Kind=="task_change")?"Изменения применены; полное поведение требует проверки":state == "literal_checks_passed" ? "Критерии запроса подтверждены по сцене" : "Проверки запроса пройдены");
            return new ChatTaskPresentation(true, "task-card-neutral", hasResults ? "Результаты действий" : "Статус задачи", "Выполнение задачи не подтверждено",
                hasResults ? "Действия перечислены ниже. Итог задачи отдельно не подтверждён." : "Применённый и проверенный результат этого запроса пока не получен.");
        }
    }
}
