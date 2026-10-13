namespace WorkoutCalculator.Web.Services;

/// <summary>Consumer wording for existing typed failure categories. Never parses translated messages.</summary>
public static class UiError
{
    public static string From(Exception error)=>Message(ApplicationError.From(error));
    public static string Message(ApplicationError error)=>error.Code switch {
        ApplicationErrorCode.Validation=>error.Message.Split('\n')[0],
        ApplicationErrorCode.StaleConflict=>"Данные изменились в другой вкладке. Ваш ввод сохранён на экране. Перезагрузите страницу и проверьте актуальные записи перед повтором.",
        ApplicationErrorCode.CorruptOrFutureSchema=>"Эти данные не удалось прочитать. Исходные записи сохранены; запись заблокирована. Откройте «Профиль → Данные» для резервной копии и восстановления.",
        ApplicationErrorCode.StorageQuota=>"Недостаточно места для сохранения. Прежние данные сохранены. Скачайте резервную копию в профиле и освободите место перед повтором.",
        ApplicationErrorCode.RecoveryRequired=>"Нужно завершить восстановление данных. Перезагрузите приложение; если фото защищены PIN, разблокируйте их в разделе «Фото» и продолжите обработку замера.",
        ApplicationErrorCode.UnsupportedBrowser=>"Этот браузер не поддерживает необходимое локальное хранение. Используйте поддерживаемый браузер и восстановите резервную копию.",
        ApplicationErrorCode.MissingAsset=>"Не удалось загрузить файл модели. Записи сохранены; повторите после загрузки приложения.",
        ApplicationErrorCode.InsufficientEvidence=>"Данных пока недостаточно. Добавьте известные замеры и подтвердите дни с полностью записанным питанием.",
        ApplicationErrorCode.PhotoQualityConflict=>"Фото не прошло проверку качества. Допустимые фактические замеры сохраняются отдельно от обновления аватара.",
        ApplicationErrorCode.UnsupportedCapability or ApplicationErrorCode.ResearchDisabled=>"Этот способ визуализации недоступен. Используйте доступную 3D-модель и фактические показатели.",
        _=>"Не удалось завершить действие. Проверьте сохранённые записи перед повтором. Не очищайте данные браузера; резервная копия доступна в профиле."
    };
}
