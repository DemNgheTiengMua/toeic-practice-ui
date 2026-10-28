function playAudio(questionId) {
    const container = document.getElementById('exam-audio-q-' + questionId);
    if (container) {
      container.scrollIntoView({ behavior: 'smooth', block: 'center' });
      const btn = container.querySelector('.exam-audio__btn-play');
      if (btn) btn.click();
    }
  }
document.querySelectorAll("[data-play-question-id]").forEach(button => {
  button.addEventListener("click", () => playAudio(button.dataset.playQuestionId));
});
